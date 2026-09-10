using System.Net;
using System.Text;
using System.Text.Json;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// Delivers to Apple Push Notification service over HTTP/2, one request per device token or one per
/// broadcast channel, authenticated with a provider token rather than a certificate.
/// </summary>
public sealed class ApnsTransport : IPushBroadcastTransport, IDisposable
{
    private const string ProductionHost = "https://api.push.apple.com";
    private const string SandboxHost = "https://api.sandbox.push.apple.com";

    /// <summary>How many requests are in flight at once. APNs multiplexes them over one connection.</summary>
    private const int Concurrency = 32;

    private readonly ApplePushOptions _options;
    private readonly ApnsJwt _jwt;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    /// <summary>Creates a transport that talks to Apple.</summary>
    /// <param name="options">Credentials and the default environment.</param>
    /// <param name="timeProvider">The clock the provider token is aged against.</param>
    /// <param name="httpClient">A client to use instead of the built-in HTTP/2 one; for tests.</param>
    public ApnsTransport(ApplePushOptions options, TimeProvider? timeProvider = null, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _jwt = new ApnsJwt(options, timeProvider ?? TimeProvider.System);
        _ownsClient = httpClient is null;
        _client = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            // APNs keeps the connection open and expects many streams over it.
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            EnableMultipleHttp2Connections = true,
        })
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
    }

    /// <inheritdoc />
    public PushPlatform Platform => PushPlatform.Apple;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PushDelivery>> SendAsync(
        IReadOnlyList<PushInstallation> installations,
        PushEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installations);
        ArgumentNullException.ThrowIfNull(message);

        var deliveries = new PushDelivery[installations.Count];
        using var slots = new SemaphoreSlim(Concurrency);

        var sends = installations.Select(async (installation, index) =>
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
        });

        await Task.WhenAll(sends);
        return deliveries;
    }

    private async Task<PushDelivery> SendOneAsync(
        PushInstallation installation, PushEnvelope message, CancellationToken cancellationToken)
    {
        var host = HostFor(installation);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/3/device/{installation.Handle}")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent(message.Json, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation("authorization", $"bearer {_jwt.Token}");
        request.Headers.TryAddWithoutValidation("apns-topic", message.ApnsTopic ?? _options.BundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", message.ApnsPushType ?? "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", message.Priority.ToString());

        if (message.CollapseId is { } collapse)
            request.Headers.TryAddWithoutValidation("apns-collapse-id", collapse);

        if (message.Expiration is { } expiration)
            request.Headers.TryAddWithoutValidation("apns-expiration", expiration.ToUnixTimeSeconds().ToString());

        return await DeliverAsync(installation.Id, request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PushDelivery> BroadcastAsync(string channel, PushEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(message);

        var host = _options.ChannelEnvironment(message.ApnsEnvironment) == ApnsEnvironment.Sandbox ? SandboxHost : ProductionHost;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/4/broadcasts/apps/{_options.BundleId}")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent(message.Json, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation("authorization", $"bearer {_jwt.Token}");
        request.Headers.TryAddWithoutValidation("apns-channel-id", channel);
        request.Headers.TryAddWithoutValidation("apns-push-type", message.ApnsPushType ?? "liveactivity");
        request.Headers.TryAddWithoutValidation("apns-priority", message.Priority.ToString());

        // Required on a broadcast, unlike a device push. 0 means one attempt; a later time is refused
        // by a channel created to store nothing.
        request.Headers.TryAddWithoutValidation("apns-expiration", (message.Expiration?.ToUnixTimeSeconds() ?? 0).ToString());

        return await DeliverAsync(channel, request, cancellationToken);
    }

    private async Task<PushDelivery> DeliverAsync(string id, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return new PushDelivery(id, Platform, PushStatus.Sent);

            var reason = await ReasonAsync(response, cancellationToken);
            return new PushDelivery(id, Platform, StatusFor(response.StatusCode, reason), reason);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new PushDelivery(id, Platform, PushStatus.Failed, e.Message);
        }
    }

    private string HostFor(PushInstallation installation)
    {
        var environment = _options.Environment == ApnsEnvironment.PerInstallation
            ? installation.Environment ?? ApnsEnvironment.Production
            : _options.Environment;

        return environment == ApnsEnvironment.Sandbox ? SandboxHost : ProductionHost;
    }

    /// <summary>
    /// Maps what Apple said to what the register should do. A dead token is worth removing; a 429 or
    /// a 503 is worth retrying; everything else is a failure the caller should see.
    /// </summary>
    internal static PushStatus StatusFor(HttpStatusCode code, string? reason) => reason switch
    {
        // Not DeviceTokenNotForTopic: that token is alive, it was sent to the wrong topic — a fault
        // in the send, not in the registration. Treating it as dead unregisters a working device.
        "BadDeviceToken" or "Unregistered" => PushStatus.Invalid,
        "TooManyRequests" => PushStatus.Throttled,
        _ => code switch
        {
            HttpStatusCode.Gone => PushStatus.Invalid,
            HttpStatusCode.TooManyRequests => PushStatus.Throttled,
            HttpStatusCode.ServiceUnavailable => PushStatus.Throttled,
            _ => PushStatus.Failed,
        },
    };

    internal static async Task<string?> ReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body)) return null;

            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : body;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
