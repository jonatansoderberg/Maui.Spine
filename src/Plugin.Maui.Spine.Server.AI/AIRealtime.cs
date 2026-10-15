using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>What the server asks a realtime provider to set up.</summary>
/// <param name="Feature">The feature's name.</param>
/// <param name="Model">The model, or for Azure OpenAI the deployment.</param>
/// <param name="Instructions">The feature's <see cref="AIFeatureOptions.SystemPrompt"/>.</param>
/// <param name="MaxOutputTokens">The feature's cap on each response.</param>
/// <param name="Options">The feature's session.</param>
/// <param name="Caller">Who the session is for.</param>
public sealed record AIRealtimeRequest(
    string Feature, string Model, string? Instructions, int? MaxOutputTokens, AIRealtimeOptions Options, AICaller Caller);

/// <summary>What the app connects with: a WebSocket to <see cref="Url"/> with <see cref="Token"/> as its bearer.</summary>
/// <param name="Protocol">The events spoken on the socket, such as <c>openai</c> for OpenAI and Azure OpenAI.</param>
/// <param name="Url">The provider's WebSocket.</param>
/// <param name="Token">A short-lived token, never the provider's key. It opens connections until <paramref name="ExpiresAt"/>.</param>
/// <param name="ExpiresAt">When the token can no longer start a session.</param>
public sealed record AIRealtimeSession(string Protocol, string Url, string Token, DateTimeOffset ExpiresAt)
{
    /// <summary>The provider's id for the session, for the log. Not sent to the app.</summary>
    [JsonIgnore]
    public string? ProviderSessionId { get; init; }
}

/// <summary>Sets up realtime sessions with one provider. Register one with <see cref="SpineAIOptions.Realtime"/>.</summary>
/// <remarks>
/// Microsoft.Extensions.AI's <c>IRealtimeClient</c> connects to a session; it does not make tokens, so this is Spine's
/// own. An implementation calls the provider with the server's key and returns what the app needs to connect.
/// </remarks>
public interface IAIRealtimeProvider
{
    /// <summary>Asks the provider for a session token.</summary>
    /// <param name="request">The feature's model and session.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the app connects with.</returns>
    Task<AIRealtimeSession> CreateSessionAsync(AIRealtimeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The built-in realtime providers.</summary>
public static class AIRealtimeProviders
{
    /// <summary>The events OpenAI's and Azure OpenAI's realtime WebSockets speak.</summary>
    public const string OpenAIProtocol = "openai";

    private static readonly HttpClient Shared = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

    /// <summary>
    /// OpenAI's realtime API: <c>POST /v1/realtime/client_secrets</c> with <c>ApiKey</c> from the provider's section, and
    /// an optional <c>Endpoint</c> (<c>https://api.openai.com/v1/</c> by default).
    /// </summary>
    /// <param name="provider">The provider's section under <c>Ai:Providers</c>.</param>
    /// <returns>The provider.</returns>
    public static IAIRealtimeProvider OpenAI(IConfiguration provider) => OpenAI(provider, Shared);

    /// <summary>
    /// Azure OpenAI's realtime API: <c>POST {Endpoint}/openai/v1/realtime/client_secrets</c> with <c>ApiKey</c> from the
    /// provider's section. The feature's <c>Model</c> is the deployment's name.
    /// </summary>
    /// <param name="provider">The provider's section under <c>Ai:Providers</c>: <c>Endpoint</c> is the resource, such as <c>https://my-resource.openai.azure.com</c>.</param>
    /// <returns>The provider.</returns>
    public static IAIRealtimeProvider AzureOpenAI(IConfiguration provider) => AzureOpenAI(provider, Shared);

    internal static IAIRealtimeProvider OpenAI(IConfiguration provider, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var endpoint = provider["Endpoint"] is { Length: > 0 } custom ? custom : "https://api.openai.com/v1/";
        return new OpenAIRealtimeProvider(http, BaseOf(endpoint), KeyOf(provider), azure: false);
    }

    internal static IAIRealtimeProvider AzureOpenAI(IConfiguration provider, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var resource = provider["Endpoint"] is { Length: > 0 } endpoint
            ? endpoint
            : throw new InvalidOperationException("Azure OpenAI needs the resource under Endpoint, such as https://my-resource.openai.azure.com.");
        return new OpenAIRealtimeProvider(http, BaseOf(resource.TrimEnd('/') + "/openai/v1/"), KeyOf(provider), azure: true);
    }

    private static Uri BaseOf(string endpoint) => new(endpoint.EndsWith('/') ? endpoint : endpoint + "/");

    private static string KeyOf(IConfiguration provider) =>
        provider["ApiKey"] is { Length: > 0 } key ? key : throw new InvalidOperationException("the provider has no ApiKey.");
}

internal sealed class OpenAIRealtimeProvider(HttpClient http, Uri root, string key, bool azure) : IAIRealtimeProvider
{
    private const int Rate = 24_000;

    public async Task<AIRealtimeSession> CreateSessionAsync(AIRealtimeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(root, "realtime/client_secrets"))
        {
            Content = new StringContent(Body(request).ToJsonString(), Encoding.UTF8, "application/json"),
        };

        if (azure) message.Headers.Add("api-key", key);
        else message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await http.SendAsync(message, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        // The provider's answer goes into the exception, which the endpoint logs and does not pass to the app.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} from {message.RequestUri}: {Shorten(text)}", null, response.StatusCode);

        using var json = JsonDocument.Parse(text);
        var answer = json.RootElement;

        var token = answer.TryGetProperty("value", out var value) && value.GetString() is { Length: > 0 } secret
            ? secret
            : throw new HttpRequestException($"The answer from {message.RequestUri} has no value: {Shorten(text)}");

        var expiresAt = answer.TryGetProperty("expires_at", out var expires) && expires.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow.AddSeconds(request.Options.TokenSeconds);

        var sessionId = answer.TryGetProperty("session", out var session) && session.ValueKind == JsonValueKind.Object
            && session.TryGetProperty("id", out var id) ? id.GetString() : null;

        return new AIRealtimeSession(AIRealtimeProviders.OpenAIProtocol, SocketOf(request.Model), token, expiresAt)
        {
            ProviderSessionId = sessionId,
        };
    }

    internal static JsonObject Body(AIRealtimeRequest request)
    {
        var options = request.Options;
        var pcm = new JsonObject { ["type"] = "audio/pcm", ["rate"] = Rate };

        var input = new JsonObject { ["format"] = pcm.DeepClone() };

        if (options.TurnDetection is { Length: > 0 } turns)
        {
            input["turn_detection"] = string.Equals(turns, "none", StringComparison.OrdinalIgnoreCase)
                ? null
                : new JsonObject { ["type"] = turns };

            if (input["turn_detection"] is JsonObject detection && options.Eagerness is { Length: > 0 } eagerness)
                detection["eagerness"] = eagerness;
        }

        if (options.NoiseReduction is { Length: > 0 } noise)
            input["noise_reduction"] = new JsonObject { ["type"] = noise };

        if (options.TranscriptionModel is { Length: > 0 } transcription)
        {
            var transcribe = new JsonObject { ["model"] = transcription };
            if (options.Language is { Length: > 0 } language) transcribe["language"] = language;
            input["transcription"] = transcribe;
        }

        var output = new JsonObject { ["format"] = pcm };
        if (options.Voice is { Length: > 0 } voice) output["voice"] = voice;

        var session = new JsonObject
        {
            ["type"] = "realtime",
            ["model"] = request.Model,
            ["audio"] = new JsonObject { ["input"] = input, ["output"] = output },
        };

        if (request.Instructions is { Length: > 0 } instructions) session["instructions"] = instructions;
        if (request.MaxOutputTokens is { } maxTokens) session["max_output_tokens"] = maxTokens;

        return new JsonObject
        {
            ["expires_after"] = new JsonObject
            {
                ["anchor"] = "created_at",
                ["seconds"] = Math.Clamp(options.TokenSeconds, 10, 7200),
            },
            ["session"] = session,
        };
    }

    private string SocketOf(string model)
    {
        var socket = new UriBuilder(new Uri(root, "realtime"))
        {
            Scheme = root.Scheme == Uri.UriSchemeHttp ? "ws" : "wss",
            Port = root.IsDefaultPort ? -1 : root.Port,
            Query = "model=" + Uri.EscapeDataString(model),
        };

        return socket.Uri.ToString();
    }

    private static string Shorten(string text) =>
        text.Length <= 500 ? text : string.Create(CultureInfo.InvariantCulture, $"{text[..500]}…");
}
