using System.Net;
using System.Text.Json;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Voice;

/// <summary>A realtime session's token from the app's backend (<c>MapSpineRealtimeSessions</c>).</summary>
/// <param name="Feature">The server's feature.</param>
/// <param name="Protocol">The events spoken on the socket: <c>openai</c> for OpenAI and Azure OpenAI.</param>
/// <param name="Url">The provider's WebSocket.</param>
/// <param name="Token">The short-lived bearer token for <paramref name="Url"/>.</param>
/// <param name="ExpiresAt">When the token can no longer open a connection.</param>
/// <param name="MaxMinutes">How long the server allows the session; the client ends it then.</param>
public sealed record VoiceSessionToken(string Feature, string Protocol, Uri Url, string Token, DateTimeOffset ExpiresAt, double? MaxMinutes, string? Voice = null);

/// <summary>Asks the backend for a session's token, with the app's sign-in and installation id.</summary>
internal sealed class VoiceTokenClient(SpineVoiceOptions options, Func<CancellationToken, Task<string?>> installationId)
{
    // One client for every session; a handler of the app's own (tests, pinning) gets a client of its own.
    private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

    private readonly Lazy<HttpClient> _http = options.HttpHandler is { } handler
        ? new(() => new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) })
        : Shared;

    public async Task<VoiceSessionToken> RequestAsync(string feature, CancellationToken cancellationToken, string? voice = null)
    {
        var endpoint = options.SessionEndpoint
            ?? throw new InvalidOperationException(
                "A voice session needs SpineVoiceOptions.SessionEndpoint: set it in UseSpineVoice to the prefix given to MapSpineRealtimeSessions.");

        var url = new Uri($"{endpoint.ToString().TrimEnd('/')}/realtime/{Uri.EscapeDataString(feature)}"
            + (string.IsNullOrWhiteSpace(voice) ? "" : $"?voice={Uri.EscapeDataString(voice)}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (options.AuthorizationHeader is { } authorization && await authorization(cancellationToken) is { Length: > 0 } header)
            request.Headers.TryAddWithoutValidation("Authorization", header);
        if (await installationId(cancellationToken) is { Length: > 0 } installation)
            request.Headers.TryAddWithoutValidation(SpineInstallation.Header, installation);

        using var response = await _http.Value.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = TryParse(text);

        if (response.IsSuccessStatusCode && json is not null
            && Text(json, "protocol") is { } protocol && Uri.TryCreate(Text(json, "url"), UriKind.Absolute, out var socket) && Text(json, "token") is { } token)
        {
            var expires = json.RootElement.TryGetProperty("expiresAt", out var e) && e.TryGetDateTimeOffset(out var at) ? at : DateTimeOffset.MaxValue;
            var minutes = json.RootElement.TryGetProperty("maxMinutes", out var m) && m.TryGetDouble(out var max) ? max : (double?)null;
            return new VoiceSessionToken(Text(json, "feature") ?? feature, protocol, socket, token, expires, minutes, Text(json, "voice"));
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
