using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.AI;

/// <summary>
/// A feature on the app's Spine.Server.AI backend. It posts the messages in Microsoft.Extensions.AI's own JSON and reads
/// a <see cref="ChatResponse"/>, or a stream of server-sent events with one <see cref="ChatResponseUpdate"/> each.
/// </summary>
internal sealed class SpineServerChatClient(string feature, SpineAIRuntime runtime) : IChatClient
{
    private const string DoneEvent = "done";
    private const string ErrorEvent = "error";

    internal static JsonSerializerOptions Json { get; } = CreateJson();

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(messages, options, stream: false, cancellationToken);
            using var response = await runtime.Http.SendAsync(request, cancellationToken);
            await ThrowIfRefusedAsync(response, request.RequestUri!, cancellationToken);

            var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<ChatResponse>(body, Json, cancellationToken)
                ?? throw new HttpRequestException($"The AI feature '{feature}' answered with an empty body from {request.RequestUri}.");
        }
        catch (Exception e) when (Cancelled(e, cancellationToken))
        {
            throw new OperationCanceledException(null, e, cancellationToken);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var updates = ReadAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            try
            {
                if (!await updates.MoveNextAsync()) yield break;
            }
            catch (Exception e) when (Cancelled(e, cancellationToken))
            {
                throw new OperationCanceledException(null, e, cancellationToken);
            }

            yield return updates.Current;
        }
    }

    /// <summary>
    /// Whether <paramref name="e"/> is the caller's own cancellation in another form. On iOS, NSUrlSessionHandler ends a
    /// read that is cancelled with a TimeoutException ("The request timed out"), which a Stop button must not show.
    /// </summary>
    private static bool Cancelled(Exception e, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested && e is not OperationCanceledException;

    private async IAsyncEnumerable<ChatResponseUpdate> ReadAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(messages, options, stream: true, cancellationToken);

        // Headers only: the body is read as it arrives, and disposing the response when the caller stops reading
        // closes the connection, which the server records as a stream cut short.
        using var response = await runtime.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await ThrowIfRefusedAsync(response, request.RequestUri!, cancellationToken);

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var item in SseParser.Create(body).EnumerateAsync(cancellationToken))
        {
            switch (item.EventType)
            {
                case DoneEvent:
                    yield break;

                case ErrorEvent:
                    throw new HttpRequestException(
                        $"The AI feature '{feature}' failed after it started: {ErrorMessage(item.Data)}", null, HttpStatusCode.BadGateway);

                default:
                    if (JsonSerializer.Deserialize<ChatResponseUpdate>(item.Data, Json) is { } update)
                        yield return update;
                    break;
            }
        }

        throw new HttpRequestException($"The stream from {request.RequestUri} ended without its done event; the answer may be cut short.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream, CancellationToken cancellationToken)
    {
        var backend = runtime.Options.Backend
            ?? throw new InvalidOperationException(
                $"The AI feature '{feature}' runs on the server, but SpineAIOptions.Backend is not set. Set it in UseSpineAI to the prefix given to MapSpineAI.");

        var url = new Uri($"{backend.ToString().TrimEnd('/')}/features/{Uri.EscapeDataString(feature)}");
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(Body(messages, options?.ConversationId)) { Headers = { ContentType = new("application/json") { CharSet = "utf-8" } } },
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));

        if (runtime.Options.AuthorizationHeader is { } authorization && await authorization(cancellationToken) is { Length: > 0 } header)
            request.Headers.TryAddWithoutValidation("Authorization", header);

        if (await runtime.InstallationIdAsync(cancellationToken) is { Length: > 0 } installation)
            request.Headers.TryAddWithoutValidation(SpineInstallation.Header, installation);

        return request;
    }

    /// <summary>
    /// <c>{ "messages": [...], "conversationId": … }</c>, the body Spine.Server.AI reads. Written by hand around
    /// Microsoft.Extensions.AI's own type info, so it needs no reflection on a trimmed or AOT-compiled app.
    /// </summary>
    internal static byte[] Body(IEnumerable<ChatMessage> messages, string? conversationId)
    {
        var message = Json.GetTypeInfo(typeof(ChatMessage));
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("messages");
            foreach (var m in messages) JsonSerializer.Serialize(writer, m, message);
            writer.WriteEndArray();
            if (conversationId is not null) writer.WriteString("conversationId", conversationId);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>Turns a refusal into the one shape the app handles; anything else that is not a success into the usual exception.</summary>
    private async Task ThrowIfRefusedAsync(HttpResponseMessage response, Uri url, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = TryParse(text);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound or HttpStatusCode.Forbidden when AIOffReasons.FromJson(Property(json, "off")) is { } reason:
                throw new AIFeatureOffException(feature, reason);

            case HttpStatusCode.TooManyRequests:
                throw new AIRateLimitedException(feature, RetryAfter(response));

            case HttpStatusCode.RequestEntityTooLarge when Property(json, "tooLarge") is { } limit:
                throw new AIRequestTooLargeException(feature, limit, json?.RootElement.TryGetProperty("max", out var max) == true && max.TryGetInt32(out var n) ? n : 0);
        }

        // A 404 without "off" is a route that does not exist: a wrong Backend, or a server without MapSpineAI. That must
        // not look like a feature that is switched off, so it says where it went.
        throw new HttpRequestException(
            $"The AI feature '{feature}' got {(int)response.StatusCode} {response.ReasonPhrase} from {url}: {Shorten(text)}", null, response.StatusCode);
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - DateTimeOffset.UtcNow,
        _ => null,
    };

    private static JsonDocument? TryParse(string text)
    {
        try
        {
            return text.Length > 0 ? JsonDocument.Parse(text) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Property(JsonDocument? json, string name) =>
        json?.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ErrorMessage(string data) => Property(TryParse(data), "error") ?? data;

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";

    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { WriteIndented = false };
        json.MakeReadOnly();
        return json;
    }
}
