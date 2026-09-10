using System.Net;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class WnsTransportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private const string Channel = "https://wns2-db5p.notify.windows.com/?token=AwYAAAB%2fQAhY";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        public IEnumerable<(HttpRequestMessage Request, string? Body)> ToWns => Requests.Where(r => !IsToken(r.Request));

        public IEnumerable<(HttpRequestMessage Request, string? Body)> ToEntra => Requests.Where(r => IsToken(r.Request));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request);
        }
    }

    private static bool IsToken(HttpRequestMessage request) => request.RequestUri!.Host == "login.microsoftonline.com";

    private static HttpResponseMessage TokenResponse(string token, string expiresIn = "\"86399\"") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"token_type":"Bearer","expires_in":{{expiresIn}},"access_token":"{{token}}"}"""),
        };

    private static (WnsTransport Transport, StubHandler Handler, FakeTimeProvider Time) NewTransport(
        Func<HttpRequestMessage, HttpResponseMessage> wns,
        Func<HttpResponseMessage>? entra = null)
    {
        var time = new FakeTimeProvider(Now);
        var issued = 0;
        var handler = new StubHandler(request => IsToken(request)
            ? (entra ?? (() => TokenResponse($"tok-{++issued}")))()
            : wns(request));

        var options = new WindowsPushOptions { TenantId = "tenant-1", ClientId = "client-1", ClientSecret = "secret-1" };
        return (new WnsTransport(options, time, new HttpClient(handler)), handler, time);
    }

    private static PushInstallation Installation(string id, string? handle = Channel) => new()
    {
        Id = id, Platform = PushPlatform.Windows, Handle = handle ?? Channel, UpdatedAt = Now,
    };

    private static PushEnvelope Toast => PushPayloads.Wns(new PushNotification { Title = "T", Body = "B" }, Now);

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    [Fact]
    public async Task A_toast_goes_to_the_channel_with_an_entra_token_and_the_headers_wns_reads()
    {
        var (transport, handler, _) = NewTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var _t = transport;

        var result = await transport.SendAsync([Installation("pc")], Toast);

        Assert.Equal(PushStatus.Sent, result.Single().Status);

        var (tokenRequest, form) = handler.ToEntra.Single();
        Assert.Equal("https://login.microsoftonline.com/tenant-1/oauth2/v2.0/token", tokenRequest.RequestUri!.ToString());
        Assert.Contains("grant_type=client_credentials", form);
        Assert.Contains("client_id=client-1", form);
        Assert.Contains("client_secret=secret-1", form);
        Assert.Contains("scope=https%3A%2F%2Fwns.windows.com%2F.default", form);

        var (send, _) = handler.ToWns.Single();
        Assert.Equal(Channel, send.RequestUri!.OriginalString);
        Assert.Equal(HttpMethod.Post, send.Method);
        Assert.Equal("Bearer tok-1", send.Headers.Authorization!.ToString());
        Assert.Equal("wns/toast", Header(send, "X-WNS-Type"));
        Assert.Equal("text/xml", send.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task A_raw_message_goes_as_an_octet_stream()
    {
        var (transport, handler, _) = NewTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var _t = transport;

        await transport.SendAsync([Installation("pc")], PushPayloads.WnsSilent(new Dictionary<string, string> { ["k"] = "v" }));

        var (send, body) = handler.ToWns.Single();
        Assert.Equal("wns/raw", Header(send, "X-WNS-Type"));
        Assert.Equal("application/octet-stream", send.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("\"k\":\"v\"", body);
    }

    [Fact]
    public async Task A_lifetime_becomes_the_ttl_in_seconds()
    {
        var (transport, handler, _) = NewTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var _t = transport;

        await transport.SendAsync([Installation("pc")], Toast with { Expiration = Now.AddMinutes(10) });

        Assert.Equal("600", Header(handler.ToWns.Single().Request, "X-WNS-TTL"));
    }

    [Fact]
    public async Task The_token_is_kept_until_it_is_near_its_end()
    {
        var (transport, handler, time) = NewTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var _t = transport;

        await transport.SendAsync([Installation("a")], Toast);
        await transport.SendAsync([Installation("b")], Toast);
        Assert.Single(handler.ToEntra);

        // A day's token, replaced five minutes before it runs out.
        time.Advance(TimeSpan.FromSeconds(86399) - TimeSpan.FromMinutes(4));
        await transport.SendAsync([Installation("c")], Toast);

        Assert.Equal(2, handler.ToEntra.Count());
        Assert.Equal("Bearer tok-2", handler.ToWns.Last().Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task A_refused_token_is_replaced_and_the_send_tried_once_more()
    {
        var sends = 0;
        var (transport, handler, _) = NewTransport(_ => new HttpResponseMessage(++sends == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK));
        using var _t = transport;

        var result = await transport.SendAsync([Installation("pc")], Toast);

        Assert.Equal(PushStatus.Sent, result.Single().Status);
        Assert.Equal(["Bearer tok-1", "Bearer tok-2"], handler.ToWns.Select(r => r.Request.Headers.Authorization!.ToString()));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, PushStatus.Invalid)]
    [InlineData(HttpStatusCode.Gone, PushStatus.Invalid)]
    [InlineData(HttpStatusCode.NotAcceptable, PushStatus.Throttled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PushStatus.Throttled)]
    [InlineData(HttpStatusCode.Forbidden, PushStatus.Failed)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, PushStatus.Failed)]
    public async Task What_wns_answers_decides_what_the_register_does(HttpStatusCode code, PushStatus expected)
    {
        var (transport, _, _) = NewTransport(_ =>
        {
            var response = new HttpResponseMessage(code);
            response.Headers.TryAddWithoutValidation("X-WNS-Error-Description", "described");
            response.Headers.TryAddWithoutValidation("X-WNS-Msg-ID", "3F2A");
            return response;
        });
        using var _t = transport;

        var delivery = (await transport.SendAsync([Installation("pc")], Toast)).Single();

        Assert.Equal(expected, delivery.Status);
        Assert.Equal($"{(int)code} described (msg 3F2A)", delivery.Reason);
    }

    [Theory]
    [InlineData("https://evil.example.com/?token=x")]
    [InlineData("https://notify.windows.com.evil.example.com/?token=x")]
    [InlineData("http://wns2-db5p.notify.windows.com/?token=x")]
    [InlineData("https://wns2-db5p.notify.windows.com:8443/?token=x")]
    [InlineData("not a uri")]
    public async Task An_address_outside_wns_never_gets_the_token(string handle)
    {
        var (transport, handler, _) = NewTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var _t = transport;

        var delivery = (await transport.SendAsync([Installation("pc", handle)], Toast)).Single();

        Assert.Equal(PushStatus.Invalid, delivery.Status);
        Assert.Equal("NotAWnsChannel", delivery.Reason);
        Assert.Empty(handler.ToWns);
    }

    [Fact]
    public async Task A_refused_token_request_fails_every_delivery_with_entras_reason_and_not_the_secret()
    {
        var (transport, handler, _) = NewTransport(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            () => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"invalid_client","error_description":"AADSTS7000215: Invalid client secret provided."}"""),
            });
        using var _t = transport;

        var result = await transport.SendAsync([Installation("a"), Installation("b")], Toast);

        Assert.All(result, d => Assert.Equal(PushStatus.Failed, d.Status));
        Assert.Contains("400 AADSTS7000215", result[0].Reason);
        Assert.Contains("client-1", result[0].Reason);
        Assert.DoesNotContain("secret-1", result[0].Reason);
        Assert.Empty(handler.ToWns);
    }

    [Fact]
    public async Task A_numeric_lifetime_is_read_as_well()
    {
        var (transport, handler, time) = NewTransport(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            () => TokenResponse("tok", expiresIn: "3600"));
        using var _t = transport;

        await transport.SendAsync([Installation("a")], Toast);
        time.Advance(TimeSpan.FromMinutes(50));
        await transport.SendAsync([Installation("b")], Toast);

        Assert.Single(handler.ToEntra);
    }
}
