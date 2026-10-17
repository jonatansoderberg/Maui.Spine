using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>The body of <c>POST /ai/features/{name}</c>.</summary>
/// <param name="Messages">The conversation so far, in Microsoft.Extensions.AI's JSON.</param>
/// <param name="ConversationId">The provider's conversation, for providers that keep one.</param>
public sealed record AIFeatureRequest(IList<ChatMessage>? Messages, string? ConversationId = null);

/// <summary>The body of <c>POST /ai/speech/{name}</c>.</summary>
/// <param name="Text">What to read aloud.</param>
public sealed record AISpeechBody(string? Text);

/// <summary>
/// The AI endpoints, in a form both hosts can use: ASP.NET routing calls them through
/// <see cref="SpineAIEndpointRouteBuilderExtensions.MapSpineAI"/>, and an Azure Functions isolated worker calls
/// them from its own <c>[Function]</c>s.
/// </summary>
/// <remarks>
/// <para><c>POST /ai/features/{name}</c> runs an exposed feature. The body is an <see cref="AIFeatureRequest"/>. With
/// <c>Accept: text/event-stream</c> the answer is a stream of <see cref="ChatResponseUpdate"/>s, one per event, then an
/// event named <c>done</c>; otherwise it is one <see cref="ChatResponse"/>. All JSON is Microsoft.Extensions.AI's own.</para>
/// <para><c>POST /ai/realtime/{name}</c> sets up a realtime voice session for an exposed realtime feature.</para>
/// <para><c>POST /ai/speech/{name}</c> reads text aloud with an exposed speech feature.</para>
/// <para><c>GET /ai/usage?from=&amp;to=&amp;feature=</c> reports what was spent.</para>
/// </remarks>
public static class SpineAIEndpoints
{
    private const int MaxReportDays = 366;

    internal static JsonSerializerOptions Json { get; } = CreateJson();

    /// <summary>Runs an exposed feature for the caller of <paramref name="request"/>.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="feature">The feature's name, from the route.</param>
    /// <param name="cancellationToken">Cancels the call; a client that goes away ends the stream.</param>
    /// <returns>The result to return from the host's own handler.</returns>
    /// <remarks>
    /// <para>Only features with <c>Expose: true</c> answer. Any other name gets the same 404 as a feature that is
    /// off, so the route does not reveal which features exist.</para>
    /// <para>The server owns the prompt. Only the text of user and assistant messages is kept, so the client's system and
    /// tool messages are dropped. <c>SystemPrompt</c> goes first. <c>MaxTurns</c> and <c>MaxInputTokens</c> are enforced
    /// with 413, and the client's options are ignored except the conversation id.</para>
    /// </remarks>
    public static async Task<IResult> FeatureAsync(HttpRequest request, string feature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(feature);

        var services = request.HttpContext.RequestServices;
        var features = services.GetRequiredService<AIFeatures>();
        var registry = services.GetRequiredService<AIFeatureRegistry>();

        if (!registry.Current.Features.TryGetValue(feature, out var found) || !found.Options.Expose || found.Options.Kind != AIFeatureKind.Chat)
            return new AIFeatureOffException(feature, AIOffReason.NotConfigured).ToResult();

        var caller = await features.IdentifyAsync(request);
        if (features.Chat(found.Name, caller) is not { } client)
            return new AIFeatureOffException(found.Name, features.OffReason(found.Name)).ToResult();

        AIFeatureRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<AIFeatureRequest>(request.Body, Json, cancellationToken);
        }
        catch (JsonException e)
        {
            return Results.BadRequest($"The body is not a feature request: {e.Message}");
        }

        if (body?.Messages is not { Count: > 0 } sent)
            return Results.BadRequest("The body has no messages.");

        var messages = ExposedMessages(found.Options, sent);

        if (found.Options.MaxTurns is { } maxTurns && messages.Count(m => m.Role == ChatRole.User) > maxTurns)
            return new AIRequestTooLargeException(found.Name, "turns", maxTurns).ToResult();

        if (found.Options.MaxInputTokens is { } maxTokens && AIUsageMeter.Estimate(messages) > maxTokens)
            return new AIRequestTooLargeException(found.Name, "tokens", maxTokens).ToResult();

        var options = new ChatOptions { ConversationId = body.ConversationId };
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Plugin.Maui.Spine.Server.AI");

        if (!WantsStream(request))
        {
            try
            {
                var response = await client.GetResponseAsync(messages, options, cancellationToken);
                return Results.Json(response, Json);
            }
            catch (AIFeatureException e)
            {
                return e.ToResult();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Failed(logger, found.Name, e);
            }
        }

        // The first update is awaited before the stream starts, so a refusal or a provider that fails at once still
        // gets a status code instead of a 200 with an error event.
        var updates = client.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        bool any;

        try
        {
            any = await updates.MoveNextAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await updates.DisposeAsync();
            return e is AIFeatureException refused ? refused.ToResult() : Failed(logger, found.Name, e);
        }

        return TypedResults.ServerSentEvents(StreamAsync(updates, any, logger, found.Name, cancellationToken));
    }

    /// <summary>Sets up a realtime voice session the app connects to itself, for the caller of <paramref name="request"/>.</summary>
    /// <param name="request">The incoming request. It has no body: everything about the session is the feature's configuration.</param>
    /// <param name="feature">The feature's name, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The result to return from the host's own handler: <c>{ feature, protocol, model, url, token, expiresAt, maxMinutes }</c>.
    /// The app opens a WebSocket to <c>url</c> with <c>Authorization: Bearer {token}</c> before <c>expiresAt</c>.
    /// </returns>
    /// <remarks>
    /// <para>Only features with <c>Kind: Realtime</c> and <c>Expose: true</c> answer, and only signed-in callers unless the
    /// feature sets <c>Callers: Any</c>. The provider's key stays on the server; the token opens connections only until it expires.</para>
    /// <para>Each token reserves the feature's <c>MaxMinutes</c> from its budgets when it is made.</para>
    /// </remarks>
    public static async Task<IResult> RealtimeSessionAsync(HttpRequest request, string feature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(feature);

        var services = request.HttpContext.RequestServices;
        var features = services.GetRequiredService<AIFeatures>();
        var registry = services.GetRequiredService<AIFeatureRegistry>();

        if (!registry.Current.Features.TryGetValue(feature, out var found) || !found.Options.Expose || found.Options.Kind != AIFeatureKind.Realtime)
            return new AIFeatureOffException(feature, AIOffReason.NotConfigured).ToResult();

        var caller = await features.IdentifyAsync(request);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Plugin.Maui.Spine.Server.AI");
        AIRealtimeSession? session;

        try
        {
            session = await features.RealtimeAsync(found.Name, caller, cancellationToken);
        }
        catch (AIFeatureException e)
        {
            return e.ToResult();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Failed(logger, found.Name, e);
        }

        if (session is null)
            return new AIFeatureOffException(found.Name, features.OffReason(found.Name)).ToResult();

        logger.LogInformation("AI feature '{Feature}': realtime session {Session} for {Caller}, token until {ExpiresAt:O}.",
            found.Name, session.ProviderSessionId ?? "(no id)", caller.Key, session.ExpiresAt);

        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Json(new
        {
            feature = found.Name,
            protocol = session.Protocol,
            model = found.Model,
            url = session.Url,
            token = session.Token,
            expiresAt = session.ExpiresAt,
            maxMinutes = found.Options.Realtime?.MaxMinutes ?? new AIRealtimeOptions().MaxMinutes,
        }, Json);
    }

    /// <summary>Reads text aloud with an exposed speech feature, for the caller of <paramref name="request"/>.</summary>
    /// <param name="request">The incoming request; its body is an <see cref="AISpeechBody"/>.</param>
    /// <param name="feature">The feature's name, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The result to return from the host's own handler: <c>{ feature, sampleRate, audio, visemes, durationMs }</c>, where
    /// <c>audio</c> is PCM16 little-endian mono in base64 and each viseme is <c>{ id, offsetMs }</c> in Azure Speech's
    /// alphabet of 22 (empty when the provider has none).
    /// </returns>
    /// <remarks>
    /// Only features with <c>Kind: Speech</c> and <c>Expose: true</c> answer. The voice, language and style are the
    /// feature's; a text longer than its <c>MaxCharacters</c> gets 413.
    /// </remarks>
    public static async Task<IResult> SpeechAsync(HttpRequest request, string feature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(feature);

        var services = request.HttpContext.RequestServices;
        var features = services.GetRequiredService<AIFeatures>();
        var registry = services.GetRequiredService<AIFeatureRegistry>();

        if (!registry.Current.Features.TryGetValue(feature, out var found) || !found.Options.Expose || found.Options.Kind != AIFeatureKind.Speech)
            return new AIFeatureOffException(feature, AIOffReason.NotConfigured).ToResult();

        AISpeechBody? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<AISpeechBody>(request.Body, Json, cancellationToken);
        }
        catch (JsonException e)
        {
            return Results.BadRequest($"The body is not a speech request: {e.Message}");
        }

        if (body?.Text is not { Length: > 0 } text || string.IsNullOrWhiteSpace(text))
            return Results.BadRequest("The body has no text.");

        var caller = await features.IdentifyAsync(request);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Plugin.Maui.Spine.Server.AI");
        AISpeech? speech;

        try
        {
            speech = await features.SpeechAsync(found.Name, text, caller, cancellationToken);
        }
        catch (AIFeatureException e)
        {
            return e.ToResult();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Failed(logger, found.Name, e);
        }

        if (speech is null)
            return new AIFeatureOffException(found.Name, features.OffReason(found.Name)).ToResult();

        return Results.Json(new
        {
            feature = found.Name,
            sampleRate = speech.SampleRate,
            audio = Convert.ToBase64String(speech.Pcm.Span),
            visemes = speech.Visemes.Select(v => new { id = v.Id, offsetMs = Math.Round(v.Offset.TotalMilliseconds, 1) }),
            durationMs = Math.Round(speech.Duration.TotalMilliseconds),
        }, Json);
    }

    /// <summary>Reports usage between <c>from</c> and <c>to</c>, by feature, model and day, with the top callers.</summary>
    /// <param name="request">
    /// The incoming request. Its query may hold <c>from</c> and <c>to</c> (dates or instants; by default the 30 days up to
    /// the end of today) and <c>feature</c>.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>401 unless <see cref="SpineAIOptions.AuthorizeReport"/> allows the request, otherwise the report.</returns>
    public static async Task<IResult> UsageAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var services = request.HttpContext.RequestServices;
        var options = services.GetRequiredService<SpineAIOptions>();

        if (options.AuthorizeReport is not { } authorize || !await authorize(request))
            return Results.Unauthorized();

        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var registry = services.GetRequiredService<AIFeatureRegistry>().Current;
        var to = Instant(request.Query["to"]) ?? AIPeriod.Day(time.GetUtcNow(), registry.Zone).End;
        var from = Instant(request.Query["from"]) ?? to.AddDays(-30);
        if (from >= to) return Results.BadRequest("from must be before to.");
        if ((to - from).TotalDays > MaxReportDays) return Results.BadRequest($"A report covers at most {MaxReportDays} days.");

        var feature = request.Query["feature"].ToString() is { Length: > 0 } named ? named : null;
        var store = services.GetRequiredService<IAIUsageStore>();

        var records = new List<AIUsageRecord>();
        await foreach (var record in store.QueryAsync(from, to, feature, cancellationToken))
            records.Add(record);

        return Results.Json(AIUsageReport.Of(records, from, to, registry.Configuration.Currency, registry.Zone), Json);
    }

    internal static List<ChatMessage> ExposedMessages(AIFeatureOptions feature, IEnumerable<ChatMessage> sent)
    {
        var messages = new List<ChatMessage>();
        if (feature.SystemPrompt is { Length: > 0 } system) messages.Add(new ChatMessage(ChatRole.System, system));

        foreach (var message in sent)
        {
            if (message.Role != ChatRole.User && message.Role != ChatRole.Assistant) continue;

            var text = message.Contents.OfType<TextContent>().Select(t => (AIContent)new TextContent(t.Text)).ToList();
            if (text.Count > 0) messages.Add(new ChatMessage(message.Role, text));
        }

        return messages;
    }

    private static bool WantsStream(HttpRequest request) =>
        request.Headers.Accept.ToString().Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

    private static async IAsyncEnumerable<SseItem<string>> StreamAsync(
        IAsyncEnumerator<ChatResponseUpdate> updates, bool any, ILogger logger, string feature, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var _ = updates;

        while (any)
        {
            yield return new SseItem<string>(JsonSerializer.Serialize(updates.Current, Json));

            string? failure = null;
            try
            {
                any = await updates.MoveNextAsync();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failure = e is AIFeatureException ? e.Message : "The model call failed.";
                logger.LogError(e, "AI feature '{Feature}': the stream failed after it started.", feature);
            }

            if (failure is not null)
            {
                yield return new SseItem<string>(JsonSerializer.Serialize(new { error = failure }, Json), "error");
                yield break;
            }
        }

        yield return new SseItem<string>("{}", "done");
    }

    private static IResult Failed(ILogger logger, string feature, Exception e)
    {
        // The provider's own message can name keys, quotas or internals, so it goes to the log and not to the app.
        logger.LogError(e, "AI feature '{Feature}': the model call failed.", feature);
        return Results.Problem("The model call failed.", statusCode: StatusCodes.Status502BadGateway);
    }

    private static DateTimeOffset? Instant(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { WriteIndented = false };
        json.MakeReadOnly();
        return json;
    }
}

/// <summary>Maps the AI endpoints onto an ASP.NET Core application.</summary>
public static class SpineAIEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>POST {prefix}/features/{name}</c> for exposed features, <c>POST {prefix}/speech/{name}</c> for exposed speech
    /// features and <c>GET {prefix}/usage</c> for the report.
    /// </summary>
    /// <param name="endpoints">The application's route builder.</param>
    /// <param name="prefix">The path the endpoints live under.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSpineAI(this IEndpointRouteBuilder endpoints, string prefix = "/ai")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var root = prefix.TrimEnd('/');
        endpoints.MapPost($"{root}/features/{{name}}", (HttpRequest request, string name, CancellationToken ct) => SpineAIEndpoints.FeatureAsync(request, name, ct));
        endpoints.MapPost($"{root}/speech/{{name}}", (HttpRequest request, string name, CancellationToken ct) => SpineAIEndpoints.SpeechAsync(request, name, ct));
        endpoints.MapGet($"{root}/usage", (HttpRequest request, CancellationToken ct) => SpineAIEndpoints.UsageAsync(request, ct));

        return endpoints;
    }

    /// <summary>
    /// Maps <c>POST {prefix}/realtime/{name}</c>, which hands the app a short-lived token for a realtime voice session.
    /// See <see cref="SpineAIEndpoints.RealtimeSessionAsync"/>.
    /// </summary>
    /// <param name="endpoints">The application's route builder.</param>
    /// <param name="prefix">The path the endpoint lives under.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSpineRealtimeSessions(this IEndpointRouteBuilder endpoints, string prefix = "/ai")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var root = prefix.TrimEnd('/');
        endpoints.MapPost($"{root}/realtime/{{name}}", (HttpRequest request, string name, CancellationToken ct) => SpineAIEndpoints.RealtimeSessionAsync(request, name, ct));

        return endpoints;
    }
}
