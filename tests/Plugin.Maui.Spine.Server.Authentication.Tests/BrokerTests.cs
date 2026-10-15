using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Plugin.Maui.Spine.Server.Authentication.Tests;

public sealed class BrokerTests : IAsyncLifetime
{
    private const string AppRedirect = "se.example.app:/signed-in";

    private AuthHost _host = null!;
    private readonly string _verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public async Task InitializeAsync() => _host = await AuthHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string Challenge(string? verifier = null) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier ?? _verifier)));

    private static Dictionary<string, string> QueryOf(Uri uri) =>
        QueryHelpers.ParseQuery(uri.Query).ToDictionary(p => p.Key, p => p.Value.ToString());

    private async Task<HttpResponseMessage> StartAsync(string provider = "google", string redirect = AppRedirect, string? challenge = null, string method = "S256") =>
        await _host.Http.GetAsync(QueryHelpers.AddQueryString($"/auth/{provider}/start", new Dictionary<string, string?>
        {
            ["redirect_uri"] = redirect,
            ["code_challenge"] = challenge ?? Challenge(),
            ["code_challenge_method"] = method,
            ["state"] = "app-1",
        }));

    /// <summary>Starts a Google sign-in and answers the token endpoint with an ID token for the nonce the server sent.</summary>
    private async Task<Dictionary<string, string>> StartGoogleAsync(string subject = "google-1")
    {
        var start = await StartAsync();
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var provider = QueryOf(start.Headers.Location!);

        _host.AppleEndpoints.TokenEndpoint = form => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                id_token = _host.GoogleIssuer.Token(subject, AuthHost.GoogleWeb, provider["nonce"], "bo@example.com", true),
                access_token = "x",
            }),
        };

        return provider;
    }

    private async Task<Dictionary<string, string>> BackToAppAsync(HttpResponseMessage callback)
    {
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var location = callback.Headers.Location!;
        Assert.StartsWith(AppRedirect + "?", location.OriginalString);
        return QueryOf(new Uri(location.OriginalString));
    }

    [Fact]
    public async Task Start_sends_the_browser_to_the_provider_as_the_web_client()
    {
        var start = await StartAsync();

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var location = start.Headers.Location!;
        Assert.Equal("https://accounts.google.com/authorize", location.GetLeftPart(UriPartial.Path));

        var query = QueryOf(location);
        Assert.Equal(AuthHost.GoogleWeb, query["client_id"]);
        Assert.Equal("https://api.test/auth/google/callback", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid email profile", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.NotEqual(Challenge(), query["code_challenge"]);
        Assert.NotEqual("app-1", query["state"]);
        Assert.False(query.ContainsKey("response_mode"));
    }

    [Fact]
    public async Task A_google_sign_in_goes_through_the_browser_and_back_to_the_app()
    {
        var provider = await StartGoogleAsync();

        var app = await BackToAppAsync(await _host.Http.GetAsync($"/auth/google/callback?code=provider-code&state={provider["state"]}"));
        Assert.Equal("app-1", app["state"]);

        var exchange = _host.AppleEndpoints.Calls.Single();
        Assert.Equal("provider-code", exchange.Form["code"]);
        Assert.Equal("google-secret", exchange.Form["client_secret"]);
        Assert.Equal("https://api.test/auth/google/callback", exchange.Form["redirect_uri"]);
        Assert.Equal(provider["code_challenge"], Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(exchange.Form["code_verifier"]))));

        var response = await _host.PostAsync("/auth/token", new SpineCodeExchange(app["code"], _verifier, AppRedirect));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = (await response.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;
        Assert.Equal("google", tokens.User.Provider);
        Assert.Equal("bo@example.com", tokens.User.Email);

        // The same Google account through the native sheet is the same user.
        var nonce = await _host.NonceAsync();
        var native = _host.GoogleIssuer.Token("google-1", AuthHost.GoogleClient, nonce);
        var nativeTokens = await (await _host.PostAsync("/auth/google/token", new SpineIdTokenRequest(native, nonce))).Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options);
        Assert.Equal(tokens.User.Id, nativeTokens!.User.Id);
    }

    [Fact]
    public async Task The_code_needs_the_verifier_and_the_same_redirect_and_works_once()
    {
        async Task<string> CodeAsync()
        {
            var provider = await StartGoogleAsync();
            return (await BackToAppAsync(await _host.Http.GetAsync($"/auth/google/callback?code=c&state={provider["state"]}")))["code"];
        }

        var wrongVerifier = await _host.PostAsync("/auth/token", new SpineCodeExchange(await CodeAsync(), "not-the-verifier", AppRedirect));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongVerifier.StatusCode);
        Assert.Equal(SpineAuthErrors.InvalidGrant, (await AuthHost.ProblemAsync(wrongVerifier))!.Error);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _host.PostAsync("/auth/token", new SpineCodeExchange(await CodeAsync(), _verifier, "se.example.app:/elsewhere"))).StatusCode);

        var code = await CodeAsync();
        Assert.Equal(HttpStatusCode.OK, (await _host.PostAsync("/auth/token", new SpineCodeExchange(code, _verifier, AppRedirect))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/auth/token", new SpineCodeExchange(code, _verifier, AppRedirect))).StatusCode);
    }

    [Fact]
    public async Task A_code_expires_after_two_minutes()
    {
        var provider = await StartGoogleAsync();
        var app = await BackToAppAsync(await _host.Http.GetAsync($"/auth/google/callback?code=c&state={provider["state"]}"));

        _host.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/auth/token", new SpineCodeExchange(app["code"], _verifier, AppRedirect))).StatusCode);
    }

    [Fact]
    public async Task A_state_works_once()
    {
        var provider = await StartGoogleAsync();
        await _host.Http.GetAsync($"/auth/google/callback?code=c&state={provider["state"]}");

        var again = await _host.Http.GetAsync($"/auth/google/callback?code=c&state={provider["state"]}");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("expired", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_state_is_tied_to_its_provider()
    {
        var provider = await StartGoogleAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.Http.GetAsync($"/auth/apple/callback?code=c&state={provider["state"]}")).StatusCode);
    }

    [Fact]
    public async Task A_cancel_at_the_provider_goes_back_to_the_app_as_access_denied()
    {
        var provider = QueryOf((await StartAsync()).Headers.Location!);

        var app = await BackToAppAsync(await _host.Http.GetAsync($"/auth/google/callback?error=access_denied&state={provider["state"]}"));

        Assert.Equal("access_denied", app["error"]);
        Assert.Equal("app-1", app["state"]);
        Assert.False(app.ContainsKey("code"));
    }

    [Fact]
    public async Task A_provider_that_refuses_the_code_is_a_server_error_for_the_app()
    {
        var provider = QueryOf((await StartAsync()).Headers.Location!);
        _host.AppleEndpoints.TokenEndpoint = _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_grant"}""") };

        var app = await BackToAppAsync(await _host.Http.GetAsync($"/auth/google/callback?code=c&state={provider["state"]}"));
        Assert.Equal("server_error", app["error"]);
    }

    [Fact]
    public async Task An_id_token_without_the_servers_nonce_is_refused()
    {
        var provider = QueryOf((await StartAsync()).Headers.Location!);
        _host.AppleEndpoints.TokenEndpoint = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id_token = _host.GoogleIssuer.Token("google-1", AuthHost.GoogleWeb, "someone-elses-nonce") }),
        };

        var app = await BackToAppAsync(await _host.Http.GetAsync($"/auth/google/callback?code=c&state={provider["state"]}"));
        Assert.Equal("invalid_token", app["error"]);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://evil.example/")]
    [InlineData("other.app:/signed-in")]
    [InlineData("se.example.app:/signed-in#fragment")]
    [InlineData("se.example.app:/signed-in?code=planted")]
    [InlineData("not a uri")]
    public async Task A_redirect_the_server_does_not_own_is_refused(string redirect)
    {
        var start = await StartAsync(redirect: redirect);
        Assert.Equal(HttpStatusCode.BadRequest, start.StatusCode);
    }

    [Theory]
    [InlineData("http://127.0.0.1:53124/")]
    [InlineData("http://[::1]:53124/callback")]
    public async Task A_loopback_redirect_is_allowed_for_windows(string redirect)
    {
        Assert.Equal(HttpStatusCode.Redirect, (await StartAsync(redirect: redirect)).StatusCode);
    }

    [Fact]
    public async Task The_web_clients_audience_is_not_accepted_from_a_native_request()
    {
        var nonce = await _host.NonceAsync();
        var token = _host.GoogleIssuer.Token("google-1", AuthHost.GoogleWeb, nonce);

        var response = await _host.PostAsync("/auth/google/token", new SpineIdTokenRequest(token, nonce));
        Assert.Contains("client id", (await AuthHost.ProblemAsync(response))!.Detail);
    }

    [Fact]
    public async Task A_name_is_read_only_from_apples_form()
    {
        var provider = await StartGoogleAsync();
        var app = await BackToAppAsync(await _host.Http.GetAsync(
            $"/auth/google/callback?code=c&state={provider["state"]}&user=" + Uri.EscapeDataString("""{"name":{"firstName":"Mallory"}}""")));

        var tokens = await (await _host.PostAsync("/auth/token", new SpineCodeExchange(app["code"], _verifier, AppRedirect))).Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options);
        Assert.Null(tokens!.User.DisplayName);
    }

    [Fact]
    public async Task A_code_body_of_the_wrong_shape_is_400()
    {
        var response = await _host.Http.PostAsync("/auth/token", new StringContent("""{"code":123,"codeVerifier":true}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_s256_is_accepted()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(method: "plain", challenge: _verifier[..43])).StatusCode);
    }

    [Fact]
    public async Task A_provider_without_a_web_client_has_no_browser_sign_in()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(provider: "microsoft")).StatusCode);
    }

    [Fact]
    public async Task Apple_posts_back_with_the_name_once_and_its_refresh_token_is_kept()
    {
        var start = await StartAsync(provider: "apple");
        var provider = QueryOf(start.Headers.Location!);
        Assert.Equal("form_post", provider["response_mode"]);
        Assert.Equal("name email", provider["scope"]);
        Assert.Equal(AuthHost.ServicesId, provider["client_id"]);
        Assert.False(provider.ContainsKey("code_challenge"));

        Dictionary<string, string>? exchange = null;
        _host.AppleEndpoints.TokenEndpoint = form =>
        {
            exchange = form;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    // The nonce Apple got is already the hash; Apple puts it in the token as it got it.
                    id_token = _host.AppleIssuer.Token("apple-web-1", AuthHost.ServicesId, provider["nonce"], "anna@example.com", "true"),
                    refresh_token = "apple-web-refresh",
                }),
            };
        };

        var callback = await _host.Http.PostAsync("/auth/apple/callback", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = "apple-code",
            ["state"] = provider["state"],
            ["user"] = """{"name":{"firstName":"Anna","lastName":"Svensson"},"email":"anna@example.com"}""",
        }));

        var app = await BackToAppAsync(callback);
        var tokens = (await (await _host.PostAsync("/auth/token", new SpineCodeExchange(app["code"], _verifier, AppRedirect)))
            .Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;

        Assert.Equal("Anna Svensson", tokens.User.DisplayName);
        Assert.Equal(AuthHost.ServicesId, new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(exchange!["client_secret"]).Subject);

        var login = Assert.Single(await _host.Users.GetLoginsAsync(tokens.User.Id));
        Assert.Equal("apple-web-refresh", login.ProviderRefreshToken);
        Assert.Equal(AuthHost.ServicesId, login.ProviderClientId);
    }

    [Fact]
    public async Task A_refresh_token_still_works_on_the_same_route()
    {
        var tokens = await _host.SignInWithAppleAsync();
        Assert.Equal(HttpStatusCode.OK, (await _host.PostAsync("/auth/token", new SpineRefreshRequest(tokens.RefreshToken))).StatusCode);
    }
}

public sealed class BrokerSetupTests
{
    [Fact]
    public async Task Loopback_is_refused_unless_the_server_turns_it_on()
    {
        await using var host = await AuthHost.StartAsync(a => a.AllowLoopbackRedirects = false);

        var start = await host.Http.GetAsync("/auth/google/start?redirect_uri=http://127.0.0.1:53124/&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256");
        Assert.Equal(HttpStatusCode.BadRequest, start.StatusCode);
    }

    [Fact]
    public void A_web_client_needs_a_public_url_and_a_secret()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();

        var noUrl = Assert.Throws<InvalidOperationException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = key;
            a.Google(o => { o.WebClientId = "web"; o.WebClientSecret = "s"; });
            a.UseInMemoryStore();
        }));
        Assert.Contains("PublicUrl", noUrl.Message);

        var noSecret = Assert.Throws<InvalidOperationException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.PublicUrl = "https://api.test/auth";
            a.SigningKey = key;
            a.Google(o => o.WebClientId = "web");
            a.UseInMemoryStore();
        }));
        Assert.Contains("WebClientSecret", noSecret.Message);

        var appleWithoutKey = Assert.Throws<InvalidOperationException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.PublicUrl = "https://api.test/auth";
            a.SigningKey = key;
            a.Apple(o => o.ServicesId = "se.example.web");
            a.UseInMemoryStore();
        }));
        Assert.Contains("TeamId", appleWithoutKey.Message);
    }

    [Theory]
    [InlineData("https")]
    [InlineData("javascript")]
    [InlineData("not a scheme")]
    public void A_redirect_scheme_must_be_one_an_app_can_own(string scheme)
    {
        Assert.Throws<InvalidOperationException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
            a.RedirectSchemes.Add(scheme);
            a.EmailPassword();
            a.UseInMemoryStore();
        }));
    }
}
