using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
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
/// <param name="Functions">
/// The device functions the app has and will run when the model calls them. The server offers only those the feature
/// lists and declared with <c>AddDevice</c>, and takes a result back only for a call it made.
/// </param>
public sealed record AIFeatureRequest(IList<ChatMessage>? Messages, string? ConversationId = null, IList<AIFunctionOffer>? Functions = null);

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
/// <para><c>POST /ai/realtime/{name}</c> sets up a realtime voice session for an exposed realtime feature, and
/// <c>/ai/realtime/{name}/relay</c> is the WebSocket a GPT-Live session talks through (ASP.NET Core only).</para>
/// <para><c>POST /ai/speech/{name}</c> reads text aloud with an exposed speech feature.</para>
/// <para><c>POST /ai/functions/{feature}/{name}</c> runs a server function a realtime session's model called.</para>
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
    /// <para>The one exception is a result for a device function: a tool message whose call id the server handed to this
    /// caller is taken, with the server's own record of the call, once. The app says which device functions it has in
    /// <see cref="AIFeatureRequest.Functions"/>.</para>
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

        var pending = services.GetRequiredService<AIPendingCalls>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Plugin.Maui.Spine.Server.AI");
        var messages = await ExposedMessagesAsync(found, caller, sent, pending, logger, cancellationToken);

        if (found.Options.MaxTurns is { } maxTurns && messages.Count(m => m.Role == ChatRole.User) > maxTurns)
            return new AIRequestTooLargeException(found.Name, "turns", maxTurns).ToResult();

        if (found.Options.MaxInputTokens is { } maxTokens && AIUsageMeter.Estimate(messages) > maxTokens)
            return new AIRequestTooLargeException(found.Name, "tokens", maxTokens).ToResult();

        var options = new ChatOptions { ConversationId = body.ConversationId };
        if (body.Functions is { Count: > 0 } offers)
            (options.AdditionalProperties ??= [])[FeatureFunctionsChatClient.OffersKey] = offers.Where(o => o?.Name is { Length: > 0 }).ToList();

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
    /// <param name="request">
    /// The incoming request. It has no body: everything about the session is the feature's configuration. The query may
    /// ask for a voice (<c>?voice=</c>) and a shorter limit (<c>?maxMinutes=</c>).
    /// </param>
    /// <param name="feature">The feature's name, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The result to return from the host's own handler: <c>{ feature, protocol, model, url, token, expiresAt, maxMinutes, tools }</c>.
    /// The app opens a WebSocket to <c>url</c> with <c>Authorization: Bearer {token}</c> before <c>expiresAt</c>. <c>tools</c> are
    /// the feature's functions, <c>{ name, description, parameters, runsOn, prefer }</c>; the app forwards a call to one that
    /// runs on the server to <see cref="FunctionAsync"/>. For a Live feature, <c>protocol</c> is <c>openai-live</c> and
    /// <c>url</c> is the relay's, relative to the prefix: the app opens it with its own sign-in, and the server runs the
    /// server functions itself.
    /// </returns>
    /// <remarks>
    /// <para>Only features with <c>Kind: Realtime</c> and <c>Expose: true</c> answer, and only signed-in callers unless the
    /// feature sets <c>Callers: Any</c>. The provider's key stays on the server; the token opens connections only until it expires.</para>
    /// <para>Each session reserves its limit (the feature's <c>MaxMinutes</c>, or the shorter <c>maxMinutes</c> the app
    /// asked for) from the budgets when it is set up. A Live session is settled with what it used when it ends.</para>
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
            // An app may ask for a voice (an avatar's); the feature's Voices decide whether it gets it.
            var voice = request.Query.TryGetValue("voice", out var asked) ? asked.ToString() : null;
            double? limit = request.Query.TryGetValue("maxMinutes", out var minutes)
                && double.TryParse(minutes.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var shorter) ? shorter : null;
            session = await features.RealtimeAsync(found.Name, caller, cancellationToken, voice, limit);
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
            maxMinutes = session.MaxMinutes,
            voice = session.Voice,
            tools = features.FunctionsOf(found).Select(tool => new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = tool.Parameters,
                runsOn = (string[])[.. tool.OnServer ? ["server"] : Array.Empty<string>(), .. tool.OnDevice ? ["device"] : Array.Empty<string>()],
                prefer = tool.Prefer == SpineAIFunctionSide.Server ? "server" : "device",
            }),
        }, Json);
    }

    /// <summary>
    /// The WebSocket a GPT-Live session talks through: the app connects with the ticket <see cref="RealtimeSessionAsync"/>
    /// gave it and its own sign-in, and the server opens the provider's socket with its key. ASP.NET Core only, with
    /// <c>app.UseWebSockets()</c>; <c>MapSpineRealtimeSessions</c> maps it.
    /// </summary>
    /// <param name="context">The upgrade request.</param>
    /// <param name="feature">The feature's name, from the route.</param>
    /// <param name="cancellationToken">Ends the session.</param>
    /// <returns>When the session has ended and been settled.</returns>
    /// <remarks>
    /// The ticket is good for one connection within <c>TokenSeconds</c>, for the caller it was issued to. The session
    /// holds one of the caller's <c>ConcurrentPerCaller</c> places while it runs.
    /// </remarks>
    public static async Task RealtimeRelayAsync(HttpContext context, string feature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(feature);

        var services = context.RequestServices;
        var features = services.GetRequiredService<AIFeatures>();
        var registry = services.GetRequiredService<AIFeatureRegistry>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Plugin.Maui.Spine.Server.AI");

        if (!context.WebSockets.IsWebSocketRequest)
        {
            // The usual cause is a host without the WebSocket middleware: say so rather than a bare 400.
            await Results.BadRequest("The relay is a WebSocket. Connect with a WebSocket, and call app.UseWebSockets() before mapping it.").ExecuteAsync(context);
            return;
        }

        if (!registry.Current.Features.TryGetValue(feature, out var found) || !found.Options.Expose || found.Options.Kind != AIFeatureKind.Realtime
            || found.Options.Realtime?.Protocol != AIRealtimeProtocol.Live)
        {
            await new AIFeatureOffException(feature, AIOffReason.NotConfigured).ToResult().ExecuteAsync(context);
            return;
        }

        var tickets = services.GetRequiredService<AIRealtimeTickets>();
        var ticket = tickets.Read(context.Request.Query["ticket"].ToString());
        if (ticket is null || ticket.Feature != found.Name)
        {
            await Results.Text("The ticket is missing, expired, used, or for another feature. Ask POST realtime/{feature} for a new one.", statusCode: 401)
                .ExecuteAsync(context);
            return;
        }

        var caller = await features.IdentifyAsync(context.Request);
        if (caller.Key != ticket.Caller)
        {
            logger.LogWarning("AI feature '{Feature}': a Live ticket for {Ticket} came from {Caller}.", found.Name, ticket.Caller, caller.Key);
            await Results.Text("The ticket was issued to another caller; connect with the sign-in that asked for it.", statusCode: 403).ExecuteAsync(context);
            return;
        }

        if (!tickets.TryUse(ticket))
        {
            await Results.Text("The ticket was used already. Ask POST realtime/{feature} for a new one.", statusCode: 401).ExecuteAsync(context);
            return;
        }

        if (!features.TryGetOn(found.Name, out _) || found.Realtime is not IAILiveProvider provider)
        {
            await new AIFeatureOffException(found.Name, features.OffReason(found.Name)).ToResult().ExecuteAsync(context);
            return;
        }

        var gate = services.GetRequiredService<AIBudgetGate>();
        IDisposable lease;
        try
        {
            lease = gate.Acquire(found, caller, "relay");
        }
        catch (AIFeatureException e)
        {
            await e.ToResult().ExecuteAsync(context);
            return;
        }

        using var held = lease;
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var relay = new AIRealtimeRelay(
            context, socket, found, caller, ticket, provider, features, services.GetRequiredService<SpineAIOptions>(), gate,
            services.GetRequiredService<AIUsageMeter>(), logger);
        await relay.RunAsync(cancellationToken);
    }

    /// <summary>
    /// Runs a server function a realtime session's model called, for the caller of <paramref name="request"/>. The app
    /// forwards the call here, with its own sign-in, and hands the result to the model.
    /// </summary>
    /// <param name="request">The incoming request; its body is an <see cref="AIFunctionCallBody"/>.</param>
    /// <param name="feature">The realtime feature's name, from the route.</param>
    /// <param name="function">The function's name, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The result to return from the host's own handler: <c>{ result }</c>, what the model reads. A function that needs
    /// sign-in, fails, or gets arguments its schema does not have answers 200 with <c>{ "error": …, "message": … }</c> as
    /// the result, so the model can tell the user.
    /// </returns>
    /// <remarks>
    /// Only exposed realtime features that list the function answer, and only for the callers the feature allows; any
    /// other name gets the same 404 as a feature that is off. The calls have rate and concurrency limiters of their own
    /// with the feature's limits, the feature's budgets must not be spent, and every call is logged.
    /// </remarks>
    public static async Task<IResult> FunctionAsync(HttpRequest request, string feature, string function, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(function);

        var services = request.HttpContext.RequestServices;
        var features = services.GetRequiredService<AIFeatures>();
        var registry = services.GetRequiredService<AIFeatureRegistry>();
        var options = services.GetRequiredService<SpineAIOptions>();

        if (!registry.Current.Features.TryGetValue(feature, out var found) || !found.Options.Expose || found.Options.Kind != AIFeatureKind.Realtime
            || found.Options.Functions?.Contains(function, StringComparer.Ordinal) != true || options.Functions.ServerFunction(function) is not { } declared)
            return new AIFeatureOffException(feature, AIOffReason.NotConfigured).ToResult();

        if (!features.TryGetOn(found.Name, out _))
            return new AIFeatureOffException(found.Name, features.OffReason(found.Name)).ToResult();

        AIFunctionCallBody? body;
        try
        {
            body = request.ContentLength is 0 ? null : await JsonSerializer.DeserializeAsync<AIFunctionCallBody>(request.Body, Json, cancellationToken);
        }
        catch (JsonException e)
        {
            return Results.BadRequest($"The body is not a function call: {e.Message}");
        }

        if (body?.Arguments is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) })
            return Results.BadRequest("The arguments are not a JSON object.");

        var caller = await features.IdentifyAsync(request);
        var gate = services.GetRequiredService<AIBudgetGate>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Plugin.Maui.Spine.Server.AI");

        IDisposable lease;
        try
        {
            lease = gate.Acquire(found, caller, "functions");
        }
        catch (AIFeatureException e)
        {
            return e.ToResult();
        }

        using var held = lease;
        try
        {
            await gate.CheckAsync(found, caller, cancellationToken);
        }
        catch (AIFeatureException e)
        {
            return e.ToResult();
        }

        var context = new SpineAIFunctionContext(caller, SpineAIFunctionSide.Server)
        {
            Feature = found.Name,
            User = caller.Kind == AICallerKind.SignedIn ? request.HttpContext.User : new ClaimsPrincipal(new ClaimsIdentity()),
            Services = services,
            CancellationToken = cancellationToken,
            MaxResultCharacters = found.Options.MaxToolResultCharacters ?? SpineAIFunction.DefaultMaxResultCharacters,
        };

        var arguments = body?.Arguments is { ValueKind: JsonValueKind.Object } given
            ? given.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);

        logger.LogInformation("AI feature '{Feature}': {Caller} forwarded a call to '{Function}' ({CallId}).", found.Name, caller.Key, function, body?.CallId ?? "no id");

        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { result = await declared.InvokeAsync(arguments, context) }, Json);
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

    internal static async Task<List<ChatMessage>> ExposedMessagesAsync(
        AIFeature feature, AICaller caller, IEnumerable<ChatMessage> sent, AIPendingCalls pending, ILogger logger, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();
        if (feature.Options.SystemPrompt is { Length: > 0 } system) messages.Add(new ChatMessage(ChatRole.System, system));

        var max = feature.Options.MaxToolResultCharacters ?? SpineAIFunction.DefaultMaxResultCharacters;

        foreach (var message in sent)
        {
            if (message.Role == ChatRole.Tool)
            {
                foreach (var result in message.Contents.OfType<FunctionResultContent>())
                {
                    // The call is the server's own record: the client's copy of it, and any result for a call the server
                    // did not hand to this caller, never reaches the model.
                    if (result.CallId is { Length: > 0 } id && await pending.TakeAsync(feature.Name, caller, id, cancellationToken) is { } call)
                    {
                        messages.Add(new ChatMessage(ChatRole.Assistant, [call]));
                        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(id, Capped(result.Result, call.Name, max))]));
                        logger.LogInformation("AI feature '{Feature}': the app's result for '{Function}' ({CallId}) from {Caller}.", feature.Name, call.Name, id, caller.Key);
                    }
                    else
                    {
                        logger.LogDebug("AI feature '{Feature}': dropped a function result ({CallId}) from {Caller} for a call the server did not make.", feature.Name, result.CallId, caller.Key);
                    }
                }

                continue;
            }

            if (message.Role != ChatRole.User && message.Role != ChatRole.Assistant) continue;

            var text = message.Contents.OfType<TextContent>().Select(t => (AIContent)new TextContent(t.Text)).ToList();
            if (text.Count > 0) messages.Add(new ChatMessage(message.Role, text));
        }

        return messages;
    }

    private static object? Capped(object? result, string function, int max)
    {
        var length = result switch
        {
            null => 0,
            string text => text.Length,
            JsonElement element => element.GetRawText().Length,
            _ => JsonSerializer.Serialize(result, Json.GetTypeInfo(result.GetType())).Length,
        };

        return length > max ? SpineAIFunctionResult.Error($"The result of '{function}' was too large to read.", "tooLarge") : result;
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
    /// features, <c>POST {prefix}/functions/{feature}/{name}</c> for a realtime session's server functions and
    /// <c>GET {prefix}/usage</c> for the report.
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
        endpoints.MapPost($"{root}/functions/{{feature}}/{{name}}", (HttpRequest request, string feature, string name, CancellationToken ct) => SpineAIEndpoints.FunctionAsync(request, feature, name, ct));
        endpoints.MapGet($"{root}/usage", (HttpRequest request, CancellationToken ct) => SpineAIEndpoints.UsageAsync(request, ct));

        return endpoints;
    }

    /// <summary>
    /// Maps <c>POST {prefix}/realtime/{name}</c>, which hands the app a short-lived token for a realtime voice session
    /// (<see cref="SpineAIEndpoints.RealtimeSessionAsync"/>), and the WebSocket <c>{prefix}/realtime/{name}/relay</c> a
    /// GPT-Live session talks through (<see cref="SpineAIEndpoints.RealtimeRelayAsync"/>), which needs
    /// <c>app.UseWebSockets()</c> before it.
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
        endpoints.Map($"{root}/realtime/{{name}}/relay", (HttpContext context, string name) => SpineAIEndpoints.RealtimeRelayAsync(context, name, context.RequestAborted));

        return endpoints;
    }
}
