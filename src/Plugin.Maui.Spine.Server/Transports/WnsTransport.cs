using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// Delivers to the Windows Push Notification Service: one request per channel URI, authenticated with
/// an access token from the app's Entra ID registration (§9.3).
/// </summary>
public sealed class WnsTransport : IPushTransport, IDisposable
{
    private const string Scope = "https://wns.windows.com/.default";

    /// <summary>How many requests are in flight at once.</summary>
    private const int Concurrency = 32;

    /// <summary>How long before its expiry a token is replaced, so that none runs out in the middle of a send.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly WindowsPushOptions _options;
    private readonly TimeProvider _time;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private volatile AccessToken? _token;

    private sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

    private sealed class TokenException(string message) : Exception(message);

    /// <summary>Creates a transport that talks to WNS.</summary>
    /// <param name="options">The Entra app registration to authenticate as.</param>
    /// <param name="timeProvider">The clock tokens and time-to-live are measured against.</param>
    /// <param name="httpClient">A client to use instead of the built-in one; for tests.</param>
    /// <exception cref="InvalidOperationException">A credential is missing.</exception>
    public WnsTransport(WindowsPushOptions options, TimeProvider? timeProvider = null, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        _ownsClient = httpClient is null;
        _client = httpClient ?? new HttpClient();
    }

    /// <inheritdoc />
    public PushPlatform Platform => PushPlatform.Windows;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PushDelivery>> SendAsync(
        IReadOnlyList<PushInstallation> installations,
        PushEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installations);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await TokenAsync(rejected: null, cancellationToken);
        }
        catch (TokenException e)
        {
            // Nothing can go out without a token, and every delivery says why rather than the send throwing.
            return [.. installations.Select(i => new PushDelivery(i.Id, Platform, PushStatus.Failed, e.Message))];
        }

        var deliveries = new PushDelivery[installations.Count];
        using var slots = new SemaphoreSlim(Concurrency);

        await Task.WhenAll(installations.Select(async (installation, index) =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                deliveries[index] = await SendOneAsync(installation, message, cancellationToken);
            }
            finally
            {
                slots.Release();
            }
        }));

        return deliveries;
    }

    private async Task<PushDelivery> SendOneAsync(PushInstallation installation, PushEnvelope message, CancellationToken cancellationToken)
    {
        if (!IsChannel(installation.Handle, out var channel))
            return new PushDelivery(installation.Id, Platform, PushStatus.Invalid, "NotAWnsChannel");

        try
        {
            var token = await TokenAsync(rejected: null, cancellationToken);
            var response = await PostAsync(channel, message, token, cancellationToken);

            // An expired or revoked token: one fresh token and one more try. What fails twice is not
            // the token's doing.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                response = await PostAsync(channel, message, await TokenAsync(rejected: token, cancellationToken), cancellationToken);
            }

            using (response)
            {
                return response.IsSuccessStatusCode
                    ? new PushDelivery(installation.Id, Platform, PushStatus.Sent)
                    : new PushDelivery(installation.Id, Platform, StatusFor(response.StatusCode), Reason(response));
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new PushDelivery(installation.Id, Platform, PushStatus.Failed, e.Message);
        }
    }

    private async Task<HttpResponseMessage> PostAsync(Uri channel, PushEnvelope message, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, channel) { Content = new StringContent(message.Json, Encoding.UTF8) };

        var raw = message.WnsType == "wns/raw";
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(raw ? "application/octet-stream" : "text/xml");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("X-WNS-Type", message.WnsType ?? "wns/toast");

        // WNS recommends against it: 100-continue only adds a round trip to every notification.
        request.Headers.ExpectContinue = false;

        if (message.Expiration is { } expiration)
        {
            var seconds = (long)Math.Max(1, (expiration - _time.GetUtcNow()).TotalSeconds);
            request.Headers.TryAddWithoutValidation("X-WNS-TTL", seconds.ToString(CultureInfo.InvariantCulture));
        }

        return await _client.SendAsync(request, cancellationToken);
    }

    /// <summary>The cached token, or a new one when it is near its end or is the one WNS just refused.</summary>
    private async Task<string> TokenAsync(string? rejected, CancellationToken cancellationToken)
    {
        if (Usable(_token, rejected) is { } cached) return cached;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Another send may have fetched one while this one waited; a burst of 401s fetches once.
            if (Usable(_token, rejected) is { } fetched) return fetched;

            _token = await FetchTokenAsync(cancellationToken);
            return _token.Value;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private string? Usable(AccessToken? token, string? rejected) =>
        token is not null && token.Value != rejected && token.ExpiresAt - Margin > _time.GetUtcNow() ? token.Value : null;

    private async Task<AccessToken> FetchTokenAsync(CancellationToken cancellationToken)
    {
        var url = $"https://login.microsoftonline.com/{Uri.EscapeDataString(_options.TenantId!)}/oauth2/v2.0/token";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId!,
            ["client_secret"] = _options.ClientSecret!,
            ["scope"] = Scope,
        });

        using var response = await _client.PostAsync(url, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = Parse(body);

        if (response.IsSuccessStatusCode && document is not null
            && document.RootElement.TryGetProperty("access_token", out var value) && value.GetString() is { Length: > 0 } token)
        {
            return new AccessToken(token, _time.GetUtcNow().AddSeconds(Lifetime(document.RootElement)));
        }

        var reason = document is not null && document.RootElement.TryGetProperty("error_description", out var description)
            ? description.GetString()
            : body;

        throw new TokenException(
            $"Entra refused the WNS token for tenant {_options.TenantId}, client {_options.ClientId}: {(int)response.StatusCode} {reason}");
    }

    /// <summary>The token's lifetime in seconds. Entra writes <c>expires_in</c> as a number or as a string.</summary>
    private static double Lifetime(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out var value)) return 3600;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var text)) return text;
        return 3600;
    }

    private static JsonDocument? Parse(string body)
    {
        try
        {
            return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="handle"/> is a WNS channel URI. It comes from the client's registration,
    /// and the bearer token goes wherever it points, so an address anywhere but WNS would hand the token
    /// to whoever registered it.
    /// </summary>
    internal static bool IsChannel(string? handle, [NotNullWhen(true)] out Uri? channel)
    {
        channel = Uri.TryCreate(handle, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.Host.EndsWith(".notify.windows.com", StringComparison.OrdinalIgnoreCase)
                ? uri
                : null;

        return channel is not null;
    }

    /// <summary>
    /// Maps what WNS said to what the register should do. A channel it does not know, or one past its
    /// 30 days, is worth removing — the app asks for a new one at its next launch. A 406 or a 503 is
    /// worth retrying; everything else is a failure the caller should see.
    /// </summary>
    internal static PushStatus StatusFor(HttpStatusCode code) => code switch
    {
        HttpStatusCode.NotFound or HttpStatusCode.Gone => PushStatus.Invalid,
        HttpStatusCode.NotAcceptable or HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests => PushStatus.Throttled,
        _ => PushStatus.Failed,
    };

    private static string Reason(HttpResponseMessage response)
    {
        var description = Header(response, "X-WNS-Error-Description") ?? Header(response, "X-WNS-Status");
        var id = Header(response, "X-WNS-Msg-ID");
        return $"{(int)response.StatusCode}{(description is null ? "" : $" {description}")}{(id is null ? "" : $" (msg {id})")}";
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    /// <inheritdoc />
    public void Dispose()
    {
        _tokenLock.Dispose();
        if (_ownsClient) _client.Dispose();
    }
}
