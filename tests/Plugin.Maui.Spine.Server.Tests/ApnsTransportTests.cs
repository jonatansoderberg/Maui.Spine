using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class ApnsTransportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Response(HttpStatusCode code, string? reason = null) =>
        new(code) { Content = new StringContent(reason is null ? "" : $$"""{"reason":"{{reason}}"}""") };

    private static (ApnsTransport Transport, StubHandler Handler, ECDsa Key) NewTransport(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        ApnsEnvironment environment = ApnsEnvironment.PerInstallation)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = new ApplePushOptions
        {
            TeamId = "TEAM123456",
            KeyId = "KEY1234567",
            BundleId = "com.companyname.orientera",
            PrivateKey = key.ExportPkcs8PrivateKeyPem(),
            Environment = environment,
        };

        var handler = new StubHandler(respond);
        return (new ApnsTransport(options, new FakeTimeProvider(Now), new HttpClient(handler)), handler, key);
    }

    private static PushInstallation Installation(string id, ApnsEnvironment? environment = null) => new()
    {
        Id = id, Platform = PushPlatform.Apple, Handle = $"token-{id}", Environment = environment, UpdatedAt = Now,
    };

    private static readonly PushEnvelope Envelope = new()
    {
        Json = """{"aps":{"alert":{"title":"T","body":"B"}}}""",
        ApnsPushType = "alert",
        ApnsTopic = "com.companyname.orientera",
        Priority = 10,
        CollapseId = "c1",
        Expiration = new DateTimeOffset(2026, 9, 8, 12, 30, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task A_two_hundred_is_sent()
    {
        var (transport, _, key) = NewTransport(_ => Response(HttpStatusCode.OK));
        using var _k = key;
        using var _t = transport;

        var result = await transport.SendAsync([Installation("a")], Envelope);

        Assert.Equal(PushStatus.Sent, result[0].Status);
        Assert.Equal("a", result[0].InstallationId);
    }

    [Fact]
    public async Task The_headers_apple_reads_are_all_set()
    {
        var (transport, handler, key) = NewTransport(_ => Response(HttpStatusCode.OK));
        using var _k = key;
        using var _t = transport;

        await transport.SendAsync([Installation("a")], Envelope);
        var request = handler.Requests[0];

        Assert.Equal("com.companyname.orientera", request.Headers.GetValues("apns-topic").Single());
        Assert.Equal("alert", request.Headers.GetValues("apns-push-type").Single());
        Assert.Equal("10", request.Headers.GetValues("apns-priority").Single());
        Assert.Equal("c1", request.Headers.GetValues("apns-collapse-id").Single());
        Assert.Equal(Envelope.Expiration!.Value.ToUnixTimeSeconds().ToString(),
            request.Headers.GetValues("apns-expiration").Single());
        Assert.StartsWith("bearer ", request.Headers.GetValues("authorization").Single());
        Assert.EndsWith("/3/device/token-a", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task The_host_follows_the_environment_the_installation_reported()
    {
        var (transport, handler, key) = NewTransport(_ => Response(HttpStatusCode.OK));
        using var _k = key;
        using var _t = transport;

        await transport.SendAsync(
            [Installation("sandbox", ApnsEnvironment.Sandbox), Installation("production", ApnsEnvironment.Production)],
            Envelope);

        var hosts = handler.Requests.Select(r => r.RequestUri!.Host).ToList();
        Assert.Contains("api.sandbox.push.apple.com", hosts);
        Assert.Contains("api.push.apple.com", hosts);
    }

    [Fact]
    public async Task A_pinned_environment_overrides_what_the_installation_said()
    {
        var (transport, handler, key) = NewTransport(_ => Response(HttpStatusCode.OK), ApnsEnvironment.Production);
        using var _k = key;
        using var _t = transport;

        await transport.SendAsync([Installation("a", ApnsEnvironment.Sandbox)], Envelope);

        Assert.Equal("api.push.apple.com", handler.Requests[0].RequestUri!.Host);
    }

    [Fact]
    public async Task An_installation_without_an_environment_is_treated_as_production()
    {
        var (transport, handler, key) = NewTransport(_ => Response(HttpStatusCode.OK));
        using var _k = key;
        using var _t = transport;

        await transport.SendAsync([Installation("a")], Envelope);

        Assert.Equal("api.push.apple.com", handler.Requests[0].RequestUri!.Host);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "BadDeviceToken", PushStatus.Invalid)]
    // Not Invalid: the token is alive, the send used the wrong topic. Removing the registration
    // over it would unregister a working device.
    [InlineData(HttpStatusCode.BadRequest, "DeviceTokenNotForTopic", PushStatus.Failed)]
    [InlineData(HttpStatusCode.Gone, "Unregistered", PushStatus.Invalid)]
    [InlineData(HttpStatusCode.TooManyRequests, "TooManyRequests", PushStatus.Throttled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, PushStatus.Throttled)]
    [InlineData(HttpStatusCode.BadRequest, "PayloadTooLarge", PushStatus.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, null, PushStatus.Failed)]
    public async Task Apples_answer_maps_to_a_status(HttpStatusCode code, string? reason, PushStatus expected)
    {
        var (transport, _, key) = NewTransport(_ => Response(code, reason));
        using var _k = key;
        using var _t = transport;

        var result = await transport.SendAsync([Installation("a")], Envelope);

        Assert.Equal(expected, result[0].Status);
        if (reason is not null) Assert.Equal(reason, result[0].Reason);
    }

    [Fact]
    public async Task A_transport_error_is_a_failure_rather_than_an_exception()
    {
        var (transport, _, key) = NewTransport(_ => throw new HttpRequestException("connection reset"));
        using var _k = key;
        using var _t = transport;

        var result = await transport.SendAsync([Installation("a")], Envelope);

        Assert.Equal(PushStatus.Failed, result[0].Status);
        Assert.Contains("connection reset", result[0].Reason);
    }

    [Fact]
    public async Task Every_installation_gets_its_own_request_and_its_own_delivery()
    {
        var (transport, handler, key) = NewTransport(_ => Response(HttpStatusCode.OK));
        using var _k = key;
        using var _t = transport;

        var installations = Enumerable.Range(0, 50).Select(i => Installation($"i{i}")).ToList();
        var result = await transport.SendAsync(installations, Envelope);

        Assert.Equal(50, handler.Requests.Count);
        Assert.Equal(50, result.Count);
        Assert.Equal(installations.Select(i => i.Id), result.Select(d => d.InstallationId));
    }

    [Fact]
    public async Task A_broadcast_goes_to_the_channel_with_the_headers_apple_requires()
    {
        var (transport, handler, key) = NewTransport(_ => Response(HttpStatusCode.OK), ApnsEnvironment.Sandbox);
        using var _k = key;
        using var _t = transport;

        var delivery = await transport.BroadcastAsync("Y2hhbm5lbA==", new PushEnvelope { Json = "{}", ApnsPushType = "liveactivity", Priority = 5 });

        Assert.Equal(PushStatus.Sent, delivery.Status);
        Assert.Equal("Y2hhbm5lbA==", delivery.InstallationId);

        var request = handler.Requests.Single();
        Assert.Equal("https://api.sandbox.push.apple.com/4/broadcasts/apps/com.companyname.orientera", request.RequestUri!.ToString());
        Assert.Equal("Y2hhbm5lbA==", request.Headers.GetValues("apns-channel-id").Single());
        Assert.Equal("liveactivity", request.Headers.GetValues("apns-push-type").Single());
        Assert.Equal("5", request.Headers.GetValues("apns-priority").Single());
        Assert.Equal("0", request.Headers.GetValues("apns-expiration").Single());
        Assert.False(request.Headers.Contains("apns-topic"));
    }

    [Fact]
    public async Task A_broadcast_needs_an_environment_when_the_options_leave_it_per_installation()
    {
        var (transport, handler, key) = NewTransport(_ => Response(HttpStatusCode.OK));
        using var _k = key;
        using var _t = transport;

        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.BroadcastAsync("c", new PushEnvelope { Json = "{}" }));
        await transport.BroadcastAsync("c", new PushEnvelope { Json = "{}", ApnsEnvironment = ApnsEnvironment.Production });

        Assert.StartsWith("https://api.push.apple.com/4/broadcasts/", handler.Requests.Single().RequestUri!.ToString());
    }
}
