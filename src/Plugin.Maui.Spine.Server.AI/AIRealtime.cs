using System.Globalization;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
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
    string Feature, string Model, string? Instructions, int? MaxOutputTokens, AIRealtimeOptions Options, AICaller Caller)
{
    /// <summary>The feature's functions, which the session declares to the model.</summary>
    public IReadOnlyList<AIRealtimeTool> Tools { get; init; } = [];

    /// <summary>The longest the session may run, in minutes: the feature's <c>MaxMinutes</c>, or less when the app asked.</summary>
    public double MaxMinutes { get; init; }
}

/// <summary>A function a realtime feature lists, as the token answer and the provider's session declare it.</summary>
/// <param name="Name">The name the model calls it by.</param>
/// <param name="Description">What it does, for the model.</param>
/// <param name="Parameters">The parameters' JSON schema.</param>
/// <param name="OnServer">The server runs it: the app forwards the call to <c>POST /ai/functions/{feature}/{name}</c>.</param>
/// <param name="OnDevice">The app may run it itself.</param>
/// <param name="Prefer">The side that runs it when both have it.</param>
public sealed record AIRealtimeTool(string Name, string Description, JsonElement Parameters, bool OnServer, bool OnDevice, SpineAIFunctionSide Prefer);

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

    /// <summary>The voice the session speaks with: the app's choice when the feature offers it, otherwise the feature's.</summary>
    public string? Voice { get; init; }

    /// <summary>The longest the session may run, in minutes.</summary>
    public double? MaxMinutes { get; init; }
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

/// <summary>
/// A realtime provider whose Realtime API socket the server opens with its own key for a feature with
/// <c>Realtime:Relay</c>: the app talks to the server's relay in the Realtime API's own events, gets no token, and the
/// server sees every response's usage.
/// </summary>
public interface IAIRealtimeRelayProvider
{
    /// <summary>Opens the provider's Realtime WebSocket for the feature's model with the server's key.</summary>
    /// <param name="request">The feature's session, and the caller it is for.</param>
    /// <param name="cancellationToken">Cancels connecting.</param>
    /// <returns>The open socket.</returns>
    Task<WebSocket> ConnectRealtimeAsync(AIRealtimeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The session the relay writes with <c>session.update</c>: the feature's instructions, voice, audio, turn detection,
    /// transcription and reasoning. Without tools: the relay adds the session's own.
    /// </summary>
    /// <param name="request">The feature's session.</param>
    /// <returns>The <c>session</c> object.</returns>
    JsonObject SessionOf(AIRealtimeRequest request);
}

/// <summary>
/// A realtime provider that also speaks GPT-Live, which gives apps no credential: the server's relay opens the
/// provider's WebSocket with the key and the app talks to the relay.
/// </summary>
public interface IAILiveProvider
{
    /// <summary>Opens the provider's Live WebSocket with the server's key. The relay sends <c>session.start</c> itself.</summary>
    /// <param name="request">The feature's session, and the caller it is for.</param>
    /// <param name="cancellationToken">Cancels connecting.</param>
    /// <returns>The open socket.</returns>
    Task<WebSocket> ConnectLiveAsync(AIRealtimeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The built-in realtime providers.</summary>
public static class AIRealtimeProviders
{
    /// <summary>The events OpenAI's and Azure OpenAI's realtime WebSockets speak.</summary>
    public const string OpenAIProtocol = "openai";

    /// <summary>GPT-Live's events, which the app speaks with the server's relay.</summary>
    public const string LiveProtocol = "openai-live";

    /// <summary>OpenAI's Realtime events, which the app speaks with the server's relay (<c>Realtime:Relay</c>).</summary>
    public const string RelayProtocol = "openai-relay";

    private static readonly HttpClient Shared = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

    /// <summary>
    /// OpenAI's realtime API: <c>POST /v1/realtime/client_secrets</c> with <c>ApiKey</c> from the provider's section, and
    /// an optional <c>Endpoint</c> (<c>https://api.openai.com/v1/</c> by default). It speaks GPT-Live too, at
    /// <c>wss://…/v1/live/sessions</c>.
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

internal sealed class OpenAIRealtimeProvider(HttpClient http, Uri root, string key, bool azure) : IAIRealtimeProvider, IAILiveProvider, IAIRealtimeRelayProvider
{
    private const int Rate = 24_000;

    internal Uri LiveUri => SocketUri("live/sessions", null);

    public async Task<WebSocket> ConnectRealtimeAsync(AIRealtimeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var socket = new ClientWebSocket();
        if (azure) socket.Options.SetRequestHeader("api-key", key);
        else socket.Options.SetRequestHeader("Authorization", $"Bearer {key}");
        socket.Options.SetRequestHeader("OpenAI-Safety-Identifier", SafetyIdentifier(request.Caller));

        try
        {
            await socket.ConnectAsync(new Uri(SocketOf(request.Model)), cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public JsonObject SessionOf(AIRealtimeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = Body(request);
        var session = (JsonObject)body["session"]!;
        body.Remove("session"); // detached: the relay puts it into a session.update of its own
        session.Remove("model");
        session.Remove("tools");
        session.Remove("tool_choice");
        return session;
    }

    public async Task<WebSocket> ConnectLiveAsync(AIRealtimeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var socket = new ClientWebSocket();
        if (azure) socket.Options.SetRequestHeader("api-key", key);
        else socket.Options.SetRequestHeader("Authorization", $"Bearer {key}");
        // OpenAI's safety identifier: a stable value per user that does not reveal who it is.
        socket.Options.SetRequestHeader("OpenAI-Safety-Identifier", SafetyIdentifier(request.Caller));

        try
        {
            await socket.ConnectAsync(LiveUri, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static string SafetyIdentifier(AICaller caller) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(caller.Key)))[..32];

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
        if (options.Reasoning is { Length: > 0 } effort) session["reasoning"] = new JsonObject { ["effort"] = effort };

        if (request.Tools.Count > 0)
        {
            session["tools"] = new JsonArray([.. request.Tools.Select(tool => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText()),
            })]);
            session["tool_choice"] = "auto";
        }

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

    private string SocketOf(string model) => SocketUri("realtime", "model=" + Uri.EscapeDataString(model)).ToString();

    private Uri SocketUri(string path, string? query)
    {
        var socket = new UriBuilder(new Uri(root, path))
        {
            Scheme = root.Scheme == Uri.UriSchemeHttp ? "ws" : "wss",
            Port = root.IsDefaultPort ? -1 : root.Port,
            Query = query ?? "",
        };

        return socket.Uri;
    }

    private static string Shorten(string text) =>
        text.Length <= 500 ? text : string.Create(CultureInfo.InvariantCulture, $"{text[..500]}…");
}
