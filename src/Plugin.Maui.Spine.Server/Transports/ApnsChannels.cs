using System.Net;
using System.Text;
using System.Text.Json;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// Manages APNs broadcast channels through Apple's channel management API, authenticated with the same
/// provider token as <see cref="ApnsTransport"/>.
/// </summary>
public sealed class ApnsChannels : IPushChannels, IDisposable
{
    private const string ProductionHost = "https://api-manage-broadcast.push.apple.com:2196";
    private const string SandboxHost = "https://api-manage-broadcast.sandbox.push.apple.com:2195";

    private readonly ApplePushOptions _options;
    private readonly ApnsJwt _jwt;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    /// <summary>Creates a client for Apple's channel management API.</summary>
    /// <param name="options">Credentials and the default environment.</param>
    /// <param name="timeProvider">The clock the provider token is aged against.</param>
    /// <param name="httpClient">A client to use instead of the built-in HTTP/2 one; for tests.</param>
    public ApnsChannels(ApplePushOptions options, TimeProvider? timeProvider = null, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _jwt = new ApnsJwt(options, timeProvider ?? TimeProvider.System);
        _ownsClient = httpClient is null;
        _client = httpClient ?? new HttpClient
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
    }

    /// <inheritdoc />
    public async Task<string> CreateAsync(
        PushChannelStorage storage = PushChannelStorage.None,
        ApnsEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        var body = $$"""{"message-storage-policy":{{(int)storage}},"push-type":"LiveActivity"}""";
        using var response = await SendAsync(HttpMethod.Post, "channels", environment, channel: null, body, cancellationToken);

        return response.Headers.TryGetValues("apns-channel-id", out var ids) && ids.FirstOrDefault() is { Length: > 0 } id
            ? id
            : throw new PushChannelException(response.StatusCode, null, "APNs answered the channel request without an apns-channel-id header.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(ApnsEnvironment? environment = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "all-channels", environment, channel: null, body: null, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("channels", out var channels)
            ? [.. channels.EnumerateArray().Select(c => c.GetString()).OfType<string>()]
            : [];
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string channel, ApnsEnvironment? environment = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        using var _ = await SendAsync(HttpMethod.Delete, "channels", environment, channel, body: null, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string resource, ApnsEnvironment? environment, string? channel, string? body,
        CancellationToken cancellationToken)
    {
        var host = _options.ChannelEnvironment(environment) == ApnsEnvironment.Sandbox ? SandboxHost : ProductionHost;
        var path = $"/1/apps/{_options.BundleId}/{resource}";

        using var request = new HttpRequestMessage(method, host + path) { Version = HttpVersion.Version20 };
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("authorization", $"bearer {_jwt.Token}");
        if (channel is not null) request.Headers.TryAddWithoutValidation("apns-channel-id", channel);

        var response = await _client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return response;

        using (response)
        {
            var reason = await ApnsTransport.ReasonAsync(response, cancellationToken);
            throw new PushChannelException(response.StatusCode, reason,
                $"APNs refused {method} {host}{path}: {(int)response.StatusCode} {reason ?? "without a reason"}.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
