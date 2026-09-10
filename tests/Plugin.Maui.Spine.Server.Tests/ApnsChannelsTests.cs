using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class ApnsChannelsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private const string Channel = "dHN0LXNyY2gtY2hubA==";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request);
        }
    }

    private static (ApnsChannels Channels, StubHandler Handler, ECDsa Key) NewChannels(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        ApnsEnvironment environment = ApnsEnvironment.Sandbox)
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
        return (new ApnsChannels(options, new FakeTimeProvider(Now), new HttpClient(handler)), handler, key);
    }

    private static HttpResponseMessage Created()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Created);
        response.Headers.TryAddWithoutValidation("apns-channel-id", Channel);
        return response;
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    [Fact]
    public async Task Creating_posts_the_policy_and_returns_the_id_apple_chose()
    {
        var (channels, handler, key) = NewChannels(_ => Created());
        using var _k = key;
        using var _c = channels;

        var id = await channels.CreateAsync(PushChannelStorage.MostRecent);

        Assert.Equal(Channel, id);
        var (request, body) = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api-manage-broadcast.sandbox.push.apple.com:2195/1/apps/com.companyname.orientera/channels", request.RequestUri!.ToString());
        Assert.StartsWith("bearer ", Header(request, "authorization"));
        Assert.Equal("""{"message-storage-policy":1,"push-type":"LiveActivity"}""", body);
    }

    [Fact]
    public async Task Listing_reads_the_channels_apple_returns_from_production_on_its_own_port()
    {
        var (channels, handler, key) = NewChannels(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"channels":["a==","b=="]}""") },
            ApnsEnvironment.Production);
        using var _k = key;
        using var _c = channels;

        var all = await channels.ListAsync();

        Assert.Equal(["a==", "b=="], all);
        var (request, _) = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api-manage-broadcast.push.apple.com:2196/1/apps/com.companyname.orientera/all-channels", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Deleting_names_the_channel_in_a_header()
    {
        var (channels, handler, key) = NewChannels(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var _k = key;
        using var _c = channels;

        await channels.DeleteAsync(Channel);

        var (request, _) = handler.Requests.Single();
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.EndsWith("/1/apps/com.companyname.orientera/channels", request.RequestUri!.ToString());
        Assert.Equal(Channel, Header(request, "apns-channel-id"));
    }

    [Fact]
    public async Task A_refusal_says_the_status_the_reason_and_what_was_asked()
    {
        var (channels, _, key) = NewChannels(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"reason":"BadChannelId"}"""),
        });
        using var _k = key;
        using var _c = channels;

        var refused = await Assert.ThrowsAsync<PushChannelException>(() => channels.DeleteAsync(Channel));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("BadChannelId", refused.Reason);
        Assert.Contains("400 BadChannelId", refused.Message);
        Assert.Contains("/1/apps/com.companyname.orientera/channels", refused.Message);
    }

    [Fact]
    public async Task Per_installation_options_need_the_environment_said_in_the_call()
    {
        var (channels, handler, key) = NewChannels(_ => Created(), ApnsEnvironment.PerInstallation);
        using var _k = key;
        using var _c = channels;

        await Assert.ThrowsAsync<InvalidOperationException>(() => channels.CreateAsync());
        await channels.CreateAsync(environment: ApnsEnvironment.Production);

        Assert.StartsWith("https://api-manage-broadcast.push.apple.com:2196/", handler.Requests.Single().Request.RequestUri!.ToString());
    }
}
