using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Plugin.Maui.Spine.Server.Authentication;
using Xunit;

namespace Plugin.Maui.Spine.Authentication.Tests;

/// <summary>A browser that does what the system browser would: follows the redirects through the server and the provider.</summary>
internal sealed class ScriptedBrowser(Func<Uri, Uri, Task<BrowserAnswer?>> run, Uri callback) : IBrowserSignIn
{
    public Uri? Opened { get; private set; }

    public async Task<BrowserAnswer?> AuthenticateAsync(Func<Uri, Uri> start, CancellationToken cancellationToken)
    {
        Opened = start(callback);
        return await run(Opened, callback);
    }
}

internal static class Query
{
    public static Dictionary<string, string> Of(Uri uri) =>
        QueryHelpers.ParseQuery(uri.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
}

/// <summary>The browser sign-in through a real Spine.Server.Authentication broker, with Google stood in for by a signing key.</summary>
public sealed class BrokerTests : IAsyncLifetime
{
    private const string WebClient = "google-web";
    private static readonly Uri AppCallback = new("se.example.app://callback");

    private readonly RsaSecurityKey _googleKey = new(RSA.Create(2048)) { KeyId = "g1" };
    private WebApplication _server = null!;
    private HttpClient _browserHttp = null!;
    private SpineAuthFlows _flows = null!;
    private SpineAuthSession _session = null!;
    private string? _nonceSeen;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var google = new OpenIdConnectConfiguration
        {
            Issuer = "https://accounts.google.com",
            AuthorizationEndpoint = "https://accounts.google.com/authorize",
            TokenEndpoint = "https://accounts.google.com/token",
        };
        google.SigningKeys.Add(_googleKey);

        builder.Services.AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
            a.PublicUrl = "http://localhost/auth";
            a.RedirectSchemes.Add("se.example.app");
            a.Google(o =>
            {
                o.WebClientId = WebClient;
                o.WebClientSecret = "secret";
                o.Configuration = new StaticConfigurationManager<OpenIdConnectConfiguration>(google);
            });
            a.UseInMemoryStore();
        });

        // Google's token endpoint: an ID token for the nonce the server sent in its authorize redirect.
        builder.Services.AddHttpClient(Plugin.Maui.Spine.Server.Authentication.IdTokenValidator.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id_token = GoogleToken(_nonceSeen!), access_token = "x" }),
            }));

        _server = builder.Build();
        _server.MapSpineAuthentication("/auth");
        await _server.StartAsync();

        _browserHttp = new HttpClient(_server.GetTestServer().CreateHandler()) { BaseAddress = new Uri("http://localhost/") };
        var client = new SpineAuthClient(_browserHttp, new Uri("http://localhost/auth/"));
        _session = new SpineAuthSession(client, new InMemorySpineAuthStorage(), TimeProvider.System, NullLogger.Instance);
        _flows = new SpineAuthFlows(client, _session);
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private string GoogleToken(string nonce) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "https://accounts.google.com",
        Audience = WebClient,
        Expires = DateTime.UtcNow.AddMinutes(10),
        Claims = new Dictionary<string, object> { ["sub"] = "google-1", ["nonce"] = nonce, ["email"] = "bo@example.com", ["email_verified"] = true },
        SigningCredentials = new SigningCredentials(_googleKey, SecurityAlgorithms.RsaSha256),
    });

    /// <summary>The system browser: open start, go through Google, come back through the server's callback to the app.</summary>
    private async Task<BrowserAnswer?> FollowAsync(Uri start, Uri callback)
    {
        var toGoogle = await _browserHttp.GetAsync(start);
        Assert.Equal(HttpStatusCode.Redirect, toGoogle.StatusCode);
        var google = Query.Of(toGoogle.Headers.Location!);
        _nonceSeen = google["nonce"];

        var back = await _browserHttp.GetAsync($"/auth/google/callback?code=google-code&state={Uri.EscapeDataString(google["state"])}");
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);

        var location = new Uri(back.Headers.Location!.OriginalString);
        Assert.Equal(callback.Scheme, location.Scheme);
        return new BrowserAnswer(callback, Query.Of(location));
    }

    [Fact]
    public async Task A_browser_sign_in_through_the_broker_signs_in()
    {
        var browser = new ScriptedBrowser(FollowAsync, AppCallback);

        var result = await _flows.BrowserAsync("google", browser, default);

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        Assert.Equal("google", result.User!.Provider);
        Assert.Equal("bo@example.com", result.User.Email);
        Assert.NotNull(await _session.GetAccessTokenAsync());

        var start = Query.Of(browser.Opened!);
        Assert.Equal("S256", start["code_challenge_method"]);
        Assert.Equal(AppCallback.AbsoluteUri, start["redirect_uri"]);
        Assert.Equal("/auth/google/start", browser.Opened!.AbsolutePath);
    }

    [Fact]
    public async Task A_closed_browser_is_a_cancel()
    {
        var result = await _flows.BrowserAsync("google", new ScriptedBrowser((_, _) => Task.FromResult<BrowserAnswer?>(null), AppCallback), default);
        Assert.Equal(SignInStatus.Canceled, result.Status);
    }

    [Fact]
    public async Task An_answer_with_another_state_is_refused()
    {
        var browser = new ScriptedBrowser((_, callback) =>
            Task.FromResult<BrowserAnswer?>(new BrowserAnswer(callback, new Dictionary<string, string> { ["code"] = "x", ["state"] = "forged" })), AppCallback);

        var result = await _flows.BrowserAsync("google", browser, default);

        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.Contains("state", result.Error);
        Assert.Null(_session.User);
    }

    [Fact]
    public async Task A_cancel_at_the_provider_is_a_cancel()
    {
        var browser = new ScriptedBrowser((start, callback) =>
            Task.FromResult<BrowserAnswer?>(new BrowserAnswer(callback, new Dictionary<string, string>
            {
                ["error"] = "access_denied",
                ["state"] = Query.Of(start)["state"],
            })), AppCallback);

        Assert.Equal(SignInStatus.Canceled, (await _flows.BrowserAsync("google", browser, default)).Status);
    }

    [Fact]
    public async Task A_code_traded_with_another_verifier_fails()
    {
        // An app that lost its verifier (or an attacker with only the code) gets invalid_grant from the server.
        var answer = await FollowAsync(new Uri($"http://localhost/auth/google/start?redirect_uri={Uri.EscapeDataString(AppCallback.AbsoluteUri)}&code_challenge={Pkce.Create().Challenge}&code_challenge_method=S256&state=s"), AppCallback);
        var client = new SpineAuthClient(_browserHttp, new Uri("http://localhost/auth/"));

        var exchanged = await client.CodeAsync(new SpineCodeExchange(answer!.Values["code"], Pkce.Create().Verifier, AppCallback.AbsoluteUri), default);

        Assert.Equal(401, exchanged.Status);
        Assert.Equal(SpineAuthErrors.InvalidGrant, exchanged.Error);
    }
}

/// <summary>The mode without a Spine server, against a stub OpenID Connect provider.</summary>
public sealed class IdentityProviderTests
{
    private const string Issuer = "https://id.example.com/";
    private const string ClientId = "app-client";
    private static readonly Uri AppCallback = new("se.example.app://callback");

    private readonly StubProvider _provider = new();
    private readonly SpineIdentityProviderOptions _options = new() { Authority = new Uri(Issuer), ClientId = ClientId, Name = "example" };

    private IdentityProviderClient Client() => new(new HttpClient(_provider), _options, TimeProvider.System);

    /// <summary>The browser: the provider signs the user in and returns to the app with a code and the app's state.</summary>
    private ScriptedBrowser Browser(Action<Dictionary<string, string>>? change = null) => new((start, callback) =>
    {
        var authorize = Query.Of(start);
        _provider.Nonce = authorize["nonce"];
        _provider.Verifier = null;
        _provider.Challenge = authorize["code_challenge"];
        var values = new Dictionary<string, string> { ["code"] = "provider-code", ["state"] = authorize["state"] };
        change?.Invoke(values);
        return Task.FromResult<BrowserAnswer?>(new BrowserAnswer(callback, values));
    }, AppCallback);

    [Fact]
    public async Task An_email_the_provider_does_not_vouch_for_comes_unverified()
    {
        _provider.Tamper = "email_verified";

        var (tokens, _) = await Client().SignInAsync(Browser(), default);

        Assert.Equal(("anna@example.com", false), (tokens!.User.Email, tokens.User.EmailVerified));
    }

    [Fact]
    public async Task Sign_in_with_pkce_and_the_user_from_the_id_token()
    {
        var browser = Browser();
        var (tokens, problem) = await Client().SignInAsync(browser, default);

        Assert.Null(problem);
        Assert.Equal("user-1", tokens!.User.Id);
        Assert.Equal("Anna", tokens.User.DisplayName);
        Assert.Equal(("anna@example.com", true), (tokens.User.Email, tokens.User.EmailVerified));
        Assert.Equal("example", tokens.User.Provider);
        Assert.Equal("provider-access", tokens.AccessToken);
        Assert.Equal("provider-refresh", tokens.RefreshToken);

        var authorize = Query.Of(browser.Opened!);
        Assert.Equal("https://id.example.com/authorize", browser.Opened!.GetLeftPart(UriPartial.Path));
        Assert.Equal(ClientId, authorize["client_id"]);
        Assert.Contains("offline_access", authorize["scope"]);

        // The token endpoint got the verifier behind the challenge, and the same redirect.
        var form = _provider.LastForm!;
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal(authorize["code_challenge"], Pkce.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))));
        Assert.Equal(AppCallback.AbsoluteUri, form["redirect_uri"]);
    }

    [Theory]
    [InlineData("nonce", "nonce")]
    [InlineData("aud", "another client")]
    [InlineData("iss", "another issuer")]
    [InlineData("exp", "expired")]
    public async Task An_id_token_that_does_not_fit_is_refused(string claim, string reason)
    {
        _provider.Tamper = claim;
        var (tokens, problem) = await Client().SignInAsync(Browser(), default);

        Assert.Null(tokens);
        Assert.Contains(reason, problem!.Error);
    }

    [Fact]
    public async Task A_refresh_keeps_the_refresh_token_when_the_provider_sends_none()
    {
        var client = Client();
        var (tokens, _) = await client.SignInAsync(Browser(), default);
        _provider.RotateRefresh = false;
        _provider.IncludeIdToken = false;

        var refreshed = await client.RefreshAsync(tokens!.RefreshToken, tokens.User, default);

        Assert.Equal(200, refreshed.Status);
        Assert.Equal(tokens.RefreshToken, refreshed.Tokens!.RefreshToken);
        Assert.Equal("user-1", refreshed.Tokens.User.Id);
    }

    [Fact]
    public async Task A_refused_refresh_signs_the_session_out()
    {
        var client = Client();
        var storage = new InMemorySpineAuthStorage();
        var session = new SpineAuthSession(client, storage, TimeProvider.System, NullLogger.Instance);
        var (tokens, _) = await client.SignInAsync(Browser(), default);
        await session.AcceptAsync(tokens! with { ExpiresAt = DateTimeOffset.UtcNow });

        _provider.RefuseRefresh = true;

        Assert.Null(await session.GetAccessTokenAsync());
        Assert.Null(session.User);
    }

    [Fact]
    public async Task Sign_out_revokes_at_the_provider()
    {
        var client = Client();
        var session = new SpineAuthSession(client, new InMemorySpineAuthStorage(), TimeProvider.System, NullLogger.Instance);
        var (tokens, _) = await client.SignInAsync(Browser(), default);
        await session.AcceptAsync(tokens!);

        await session.SignOutAsync();

        Assert.Equal("provider-refresh", _provider.Revoked);
    }

    [Fact]
    public async Task A_provider_that_cannot_be_reached_is_a_failure_with_its_address()
    {
        _provider.Down = true;
        var (_, problem) = await Client().SignInAsync(Browser(), default);

        Assert.Equal(SignInStatus.Failed, problem!.Status);
        Assert.Contains("id.example.com", problem.Error);
    }

    [Fact]
    public async Task A_cancel_at_the_provider_is_a_cancel()
    {
        var (_, problem) = await Client().SignInAsync(Browser(v => { v.Remove("code"); v["error"] = "access_denied"; }), default);
        Assert.Equal(SignInStatus.Canceled, problem!.Status);
    }

    [Fact]
    public async Task A_discovery_document_for_another_issuer_is_refused()
    {
        _provider.DiscoveredIssuer = "https://evil.example/";
        var (_, problem) = await Client().SignInAsync(Browser(), default);

        Assert.Contains("issuer", problem!.Error);
    }

    [Fact]
    public async Task A_token_endpoint_over_http_is_refused()
    {
        _provider.TokenEndpoint = "http://id.example.com/token";
        var (_, problem) = await Client().SignInAsync(Browser(), default);

        Assert.Contains("not https", problem!.Error);
    }

    [Fact]
    public async Task An_audience_that_is_not_a_string_is_another_client()
    {
        _provider.Tamper = "aud-number";
        var (tokens, problem) = await Client().SignInAsync(Browser(), default);

        Assert.Null(tokens);
        Assert.Contains("another client", problem!.Error);
    }

    [Fact]
    public async Task A_refresh_lost_on_the_way_keeps_the_session()
    {
        var client = Client();
        var session = new SpineAuthSession(client, new InMemorySpineAuthStorage(), TimeProvider.System, NullLogger.Instance);
        var (tokens, _) = await client.SignInAsync(Browser(), default);
        await session.AcceptAsync(tokens! with { ExpiresAt = DateTimeOffset.UtcNow });

        _provider.DropRefresh = true;

        Assert.Null(await session.GetAccessTokenAsync());
        Assert.Equal("user-1", session.User!.Id);
    }

    private sealed class StubProvider : HttpMessageHandler
    {
        public string DiscoveredIssuer { get; set; } = Issuer;
        public string TokenEndpoint { get; set; } = "https://id.example.com/token";
        public bool DropRefresh { get; set; }
        public string? Nonce { get; set; }
        public string? Verifier { get; set; }
        public string? Challenge { get; set; }
        public string? Tamper { get; set; }
        public bool RotateRefresh { get; set; } = true;
        public bool IncludeIdToken { get; set; } = true;
        public bool RefuseRefresh { get; set; }
        public bool Down { get; set; }
        public string? Revoked { get; private set; }
        public Dictionary<string, string>? LastForm { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down) throw new HttpRequestException("Connection refused");

            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("openid-configuration"))
            {
                return Json(new JsonObject
                {
                    ["issuer"] = DiscoveredIssuer,
                    ["authorization_endpoint"] = "https://id.example.com/authorize",
                    ["token_endpoint"] = TokenEndpoint,
                    ["revocation_endpoint"] = "https://id.example.com/revoke",
                });
            }

            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken)).ToDictionary(p => p.Key, p => p.Value.ToString());
            LastForm = form;

            if (path == "/revoke")
            {
                Revoked = form["token"];
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (form["grant_type"] == "refresh_token" && DropRefresh)
                throw new IOException("The connection was reset.");

            if (form["grant_type"] == "refresh_token" && RefuseRefresh)
                return Json(new JsonObject { ["error"] = "invalid_grant" }, HttpStatusCode.BadRequest);

            var answer = new JsonObject { ["access_token"] = "provider-access", ["expires_in"] = 3600, ["token_type"] = "Bearer" };
            if (RotateRefresh) answer["refresh_token"] = "provider-refresh";
            if (IncludeIdToken) answer["id_token"] = IdToken();
            return Json(answer);
        }

        // An unsigned JWT: the app takes it from the token endpoint over TLS and checks its claims, not its signature.
        private string IdToken()
        {
            var claims = new JsonObject
            {
                ["iss"] = Tamper == "iss" ? "https://evil.example/" : Issuer,
                ["aud"] = Tamper switch { "aud" => "someone-else", "aud-number" => 42, _ => ClientId },
                ["exp"] = DateTimeOffset.UtcNow.AddMinutes(Tamper == "exp" ? -10 : 10).ToUnixTimeSeconds(),
                ["sub"] = "user-1",
                ["name"] = "Anna",
                ["email"] = "anna@example.com",
                ["email_verified"] = Tamper != "email_verified",
                ["nonce"] = Tamper == "nonce" ? "other" : Nonce,
            };

            static string Part(string json) => Pkce.Base64Url(Encoding.UTF8.GetBytes(json));
            return $"{Part("{\"alg\":\"none\"}")}.{Part(claims.ToJsonString())}.";
        }

        private static HttpResponseMessage Json(JsonObject body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(answer(request));
}

public sealed class OptionsTests
{
    private static SpineAuthenticationOptions Direct(string authority) =>
        new SpineAuthenticationOptions { CallbackScheme = "se.example.app" }.UseIdentityProvider(p => { p.Authority = new Uri(authority); p.ClientId = "app"; });

    [Fact]
    public void An_authority_over_http_is_refused_except_on_loopback()
    {
        Assert.Contains("https", Assert.Throws<InvalidOperationException>(() => Direct("http://id.example.com/").Validate(returnsToScheme: true)).Message);
        Direct("http://localhost:5310/").Validate(returnsToScheme: true);
        Direct("https://id.example.com/").Validate(returnsToScheme: true);
    }

    [Fact]
    public void A_browser_sign_in_needs_a_lowercase_callback_scheme()
    {
        var options = new SpineAuthenticationOptions { Server = new Uri("https://api.test/auth") }.AddOpenIdConnect("google", "Google");
        Assert.Contains("CallbackScheme", Assert.Throws<InvalidOperationException>(() => options.Validate(returnsToScheme: true)).Message);

        options.Validate(returnsToScheme: false);   // Windows: the loopback

        options.CallbackScheme = "Se.Example.App";
        Assert.Contains("lowercase", Assert.Throws<InvalidOperationException>(() => options.Validate(returnsToScheme: false)).Message);

        options.CallbackScheme = "se.example.app";
        options.Validate(returnsToScheme: true);
    }
}
