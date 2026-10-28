using System.Net;
using System.Text.Json;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Voice;

/// <summary>A realtime session's token from the app's backend (<c>MapSpineRealtimeSessions</c>).</summary>
/// <param name="Feature">The server's feature.</param>
/// <param name="Protocol">
/// The events spoken on the socket: <c>openai</c> for OpenAI and Azure OpenAI, <c>openai-live</c> for GPT-Live through the
/// server's relay.
/// </param>
/// <param name="Url">The provider's WebSocket, or for <c>openai-live</c> the server's relay.</param>
/// <param name="Token">The short-lived bearer token for <paramref name="Url"/>; for <c>openai-live</c>, the relay's ticket, which is in <paramref name="Url"/>.</param>
/// <param name="ExpiresAt">When the token can no longer open a connection.</param>
/// <param name="MaxMinutes">How long the server allows the session; the client ends it then.</param>
public sealed record VoiceSessionToken(string Feature, string Protocol, Uri Url, string Token, DateTimeOffset ExpiresAt, double? MaxMinutes, string? Voice = null)
{
    /// <summary>The feature's functions, as the server lists them with the token: the session offers these and no others.</summary>
    public IReadOnlyList<VoiceSessionTool> Tools { get; init; } = [];

    /// <summary>How many rounds of function calls one turn of the user may take; the feature's <c>MaxToolRounds</c>.</summary>
    public int MaxToolRounds { get; init; } = 4;

    /// <summary>The longest result of a function that reaches the model; the feature's <c>MaxToolResultCharacters</c>.</summary>
    public int MaxResultCharacters { get; init; } = SpineAIFunction.DefaultMaxResultCharacters;
}

/// <summary>A function a realtime feature lists.</summary>
/// <param name="Name">The name the model calls it by.</param>
/// <param name="Description">What it does, for the model.</param>
/// <param name="Parameters">The parameters' JSON schema.</param>
/// <param name="OnServer">The server runs it: the session forwards the call to <c>POST {SessionEndpoint}/functions/{feature}/{name}</c>.</param>
/// <param name="OnDevice">The app may run it, when it has it.</param>
/// <param name="Prefer">The side that runs it when both have it.</param>
public sealed record VoiceSessionTool(string Name, string Description, JsonElement Parameters, bool OnServer, bool OnDevice, SpineAIFunctionSide Prefer);

/// <summary>Asks the backend for a session's token, with the app's sign-in and installation id.</summary>
internal sealed class VoiceTokenClient(SpineVoiceOptions options, Func<CancellationToken, Task<string?>> installationId)
{
    // One client for every session; a handler of the app's own (tests, pinning) gets a client of its own.
    private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

    private readonly Lazy<HttpClient> _http = options.HttpHandler is { } handler
        ? new(() => new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) })
        : Shared;

    public Task<string?> InstallationIdAsync(CancellationToken cancellationToken) => installationId(cancellationToken);

    /// <summary>The session's conversation, sent with every request so the server's functions see it too.</summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// How long the token request keeps trying a server that does not answer yet. An app service stopped while idle
    /// (Azure's free plan) took 54 s to start; its first requests time out or cannot connect, which is not a refusal.
    /// </summary>
    internal TimeSpan WakeTimeout { get; init; } = TimeSpan.FromSeconds(90);

    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<VoiceSessionToken> RequestAsync(string feature, CancellationToken cancellationToken, string? voice = null, double? maxMinutes = null)
    {
        var endpoint = options.SessionEndpoint
            ?? throw new InvalidOperationException(
                "A voice session needs SpineVoiceOptions.SessionEndpoint: set it in UseSpineVoice to the prefix given to MapSpineRealtimeSessions.");

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(voice)) query.Add($"voice={Uri.EscapeDataString(voice)}");
        if (maxMinutes is > 0) query.Add($"maxMinutes={maxMinutes.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        var url = new Uri($"{endpoint.ToString().TrimEnd('/')}/realtime/{Uri.EscapeDataString(feature)}"
            + (query.Count == 0 ? "" : "?" + string.Join('&', query)));
        using var response = await SendWhileWakingAsync(url, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = TryParse(text);

        if (response.IsSuccessStatusCode && json is not null
            && Text(json, "protocol") is { } protocol && SocketOf(endpoint, Text(json, "url")) is { } socket && Text(json, "token") is { } token)
        {
            var expires = json.RootElement.TryGetProperty("expiresAt", out var e) && e.TryGetDateTimeOffset(out var at) ? at : DateTimeOffset.MaxValue;
            var minutes = json.RootElement.TryGetProperty("maxMinutes", out var m) && m.TryGetDouble(out var max) ? max : (double?)null;
            var root = json.RootElement;
            return new VoiceSessionToken(Text(json, "feature") ?? feature, protocol, socket, token, expires, minutes, Text(json, "voice"))
            {
                Tools = Tools(json),
                MaxToolRounds = root.TryGetProperty("maxToolRounds", out var rounds) && rounds.TryGetInt32(out var r) && r > 0 ? r : 4,
                MaxResultCharacters = root.TryGetProperty("maxResultCharacters", out var cap) && cap.TryGetInt32(out var c) && c > 0 ? c : SpineAIFunction.DefaultMaxResultCharacters,
            };
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound or HttpStatusCode.Forbidden when AIOffReasons.FromJson(Text(json, "off")) is { } reason:
                throw new AIFeatureOffException(feature, reason);

            case HttpStatusCode.TooManyRequests:
                throw new AIRateLimitedException(feature, response.Headers.RetryAfter?.Delta);
        }

        // A 404 without "off" is a route that does not exist: a wrong SessionEndpoint, or a server without the endpoint.
        throw new HttpRequestException(
            $"The voice feature '{feature}' got {(int)response.StatusCode} {response.ReasonPhrase} from {url}: {(text.Length <= 300 ? text : text[..300] + "…")}",
            null, response.StatusCode);
    }

    /// <summary>Posts to <paramref name="url"/>, trying again while the server is starting: no connection, a timeout, or 502–504.</summary>
    private async Task<HttpResponseMessage> SendWhileWakingAsync(Uri url, CancellationToken cancellationToken)
    {
        var started = Environment.TickCount64;
        bool Waking() => Environment.TickCount64 - started < WakeTimeout.TotalMilliseconds;

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            foreach (var (name, value) in await AppHeadersAsync(cancellationToken))
                request.Headers.TryAddWithoutValidation(name, value);
            try
            {
                var response = await _http.Value.SendAsync(request, cancellationToken);
                if (response.StatusCode is not (HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout) || !Waking())
                    return response;
                response.Dispose();
            }
            catch (Exception e) when (e is HttpRequestException { StatusCode: null } or TaskCanceledException
                && !cancellationToken.IsCancellationRequested && Waking())
            {
            }
            await Task.Delay(RetryDelay, cancellationToken);
        }
    }

    /// <summary>The headers that open the session's socket: the provider's token, or for the relay the app's own sign-in.</summary>
    public async Task<IReadOnlyDictionary<string, string>> SocketHeadersAsync(VoiceSessionToken token, CancellationToken cancellationToken) =>
        token.Protocol is "openai-live" or "openai-relay"
            ? await AppHeadersAsync(cancellationToken) // the server's relay: the ticket is in the url, the app's own headers go with it
            : new Dictionary<string, string> { ["Authorization"] = $"Bearer {token.Token}" };

    private async Task<Dictionary<string, string>> AppHeadersAsync(CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.AuthorizationHeader is { } authorization && await authorization(cancellationToken) is { Length: > 0 } header)
            headers["Authorization"] = header;
        if (await installationId(cancellationToken) is { Length: > 0 } installation)
            headers[SpineInstallation.Header] = installation;
        foreach (var (name, value) in SpineClientHeaders.ForDevice(ConversationId))
            headers[name] = value;
        return headers;
    }

    /// <summary>The socket's address: absolute for a provider, relative to <see cref="SpineVoiceOptions.SessionEndpoint"/> for the server's relay.</summary>
    private static Uri? SocketOf(Uri endpoint, string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "ws" or "wss" or "http" or "https") return absolute;

        var root = endpoint.ToString().EndsWith('/') ? endpoint : new Uri(endpoint + "/");
        var relay = new UriBuilder(new Uri(root, url)) { Scheme = root.Scheme == Uri.UriSchemeHttp ? "ws" : "wss" };
        relay.Port = root.IsDefaultPort ? -1 : root.Port;
        return relay.Uri;
    }

    /// <summary>
    /// Runs a server function the model called: posts it to <c>{SessionEndpoint}/functions/{feature}/{name}</c> with the
    /// app's sign-in. Never throws for an answer: a refusal or a failure is an error result, so the model can say so.
    /// </summary>
    public async Task<object?> CallFunctionAsync(string feature, string name, string? callId, string? arguments, CancellationToken cancellationToken)
    {
        var endpoint = options.SessionEndpoint!;
        var url = new Uri($"{endpoint.ToString().TrimEnd('/')}/functions/{Uri.EscapeDataString(feature)}/{Uri.EscapeDataString(name)}");

        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["callId"] = callId,
            ["arguments"] = ParseObject(arguments),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        foreach (var (header, value) in await AppHeadersAsync(cancellationToken))
            request.Headers.TryAddWithoutValidation(header, value);

        using var response = await _http.Value.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = TryParse(text);

        if (response.IsSuccessStatusCode && json?.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("result", out var result))
            return result.Clone();

        // The server's refusals reach the model as they do from the relay, so it can tell the user why.
        switch (AIOffReasons.FromJson(Text(json, "off")))
        {
            case AIOffReason.SignInRequired:
                return SpineAIFunctionResult.SignInRequired();
            case { } off:
                return SpineAIFunctionResult.Error($"The function '{name}' is not available now ({off}).", "refused");
        }
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return SpineAIFunctionResult.Error(
                $"Too many function calls; try again in {response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60:0} seconds.", "refused");

        throw new HttpRequestException(
            $"The server function '{name}' of '{feature}' got {(int)response.StatusCode} {response.ReasonPhrase} from {url}: {(text.Length <= 300 ? text : text[..300] + "…")}",
            null, response.StatusCode);
    }

    private static System.Text.Json.Nodes.JsonNode? ParseObject(string? arguments)
    {
        try
        {
            return string.IsNullOrWhiteSpace(arguments) ? new System.Text.Json.Nodes.JsonObject() : System.Text.Json.Nodes.JsonNode.Parse(arguments);
        }
        catch (JsonException)
        {
            return new System.Text.Json.Nodes.JsonObject();
        }
    }

    private static List<VoiceSessionTool> Tools(JsonDocument json)
    {
        var tools = new List<VoiceSessionTool>();
        if (!json.RootElement.TryGetProperty("tools", out var list) || list.ValueKind != JsonValueKind.Array) return tools;

        foreach (var tool in list.EnumerateArray())
        {
            if (tool.ValueKind != JsonValueKind.Object || !tool.TryGetProperty("name", out var name) || name.GetString() is not { Length: > 0 } named) continue;

            var sides = tool.TryGetProperty("runsOn", out var runsOn) && runsOn.ValueKind == JsonValueKind.Array
                ? runsOn.EnumerateArray().Select(s => s.GetString()).ToList()
                : [];

            tools.Add(new VoiceSessionTool(
                named,
                tool.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "",
                tool.TryGetProperty("parameters", out var parameters) ? parameters.Clone() : default,
                sides.Contains("server"),
                sides.Contains("device"),
                tool.TryGetProperty("prefer", out var prefer) && prefer.GetString() == "server" ? SpineAIFunctionSide.Server : SpineAIFunctionSide.Device));
        }

        return tools;
    }

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

    private static string? Text(JsonDocument? json, string name) =>
        json?.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
