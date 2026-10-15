using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Plugin.Maui.Spine.Server.Authentication.Tests;

public sealed class IdTokenTests : IAsyncLifetime
{
    private AuthHost _host = null!;

    public async Task InitializeAsync() => _host = await AuthHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<(HttpStatusCode Status, string? Error, string? Detail)> SignInAsync(string provider, string token, string? nonce)
    {
        var response = await _host.PostAsync($"/auth/{provider}/token", new SpineIdTokenRequest(token, nonce));
        if (response.IsSuccessStatusCode) return (response.StatusCode, null, null);

        var problem = await AuthHost.ProblemAsync(response);
        return (response.StatusCode, problem?.Error, problem?.Detail);
    }

    [Fact]
    public async Task An_apple_token_signs_in_and_the_access_token_opens_protected_routes()
    {
        var tokens = await _host.SignInWithAppleAsync();

        Assert.Equal("apple", tokens.User.Provider);
        Assert.Equal("anna@example.com", tokens.User.Email);
        Assert.True(tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(14));

        var me = await _host.WithBearerAsync(HttpMethod.Get, "/me", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(tokens.User.Id, await me.Content.ReadAsStringAsync());

        var access = new JsonWebToken(tokens.AccessToken);
        Assert.Equal("ES256", access.Alg);
        Assert.Equal("https://api.test", access.Issuer);
        Assert.Equal("apple", access.GetClaim(SpineAuthDefaults.ProviderClaim).Value);

        var user = await _host.Users.FindByIdAsync(tokens.User.Id);
        Assert.True(user!.EmailVerified);
    }

    [Fact]
    public async Task The_same_subject_is_the_same_user()
    {
        var first = await _host.SignInWithAppleAsync();
        var second = await _host.SignInWithAppleAsync();

        Assert.Equal(first.User.Id, second.User.Id);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
    }

    [Fact]
    public async Task A_nonce_works_once()
    {
        var nonce = await _host.NonceAsync();
        var token = _host.AppleIssuer.Token("apple-001", AuthHost.Bundle, FakeIssuer.AppleNonce(nonce));

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync("apple", token, nonce)).Status);

        var again = await SignInAsync("apple", token, nonce);
        Assert.Equal(HttpStatusCode.Unauthorized, again.Status);
        Assert.Equal(SpineAuthErrors.InvalidToken, again.Error);
        Assert.Contains("nonce", again.Detail);
    }

    [Fact]
    public async Task Apple_needs_the_hashed_nonce_in_the_token()
    {
        var nonce = await _host.NonceAsync();
        var raw = _host.AppleIssuer.Token("apple-001", AuthHost.Bundle, nonce);

        Assert.Contains("nonce", (await SignInAsync("apple", raw, nonce)).Detail);
    }

    [Fact]
    public async Task A_nonce_the_server_never_made_is_refused()
    {
        var token = _host.AppleIssuer.Token("apple-001", AuthHost.Bundle, FakeIssuer.AppleNonce("made-up"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync("apple", token, "made-up")).Status);
    }

    [Fact]
    public async Task A_token_without_a_nonce_is_refused_when_one_is_required()
    {
        var token = _host.AppleIssuer.Token("apple-001", AuthHost.Bundle);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync("apple", token, null)).Status);
    }

    [Fact]
    public async Task A_token_for_another_app_is_refused()
    {
        var nonce = await _host.NonceAsync();
        var token = _host.AppleIssuer.Token("apple-001", "se.someone.else", FakeIssuer.AppleNonce(nonce));

        Assert.Contains("client id", (await SignInAsync("apple", token, nonce)).Detail);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var nonce = await _host.NonceAsync();
        var token = _host.AppleIssuer.Token("apple-001", AuthHost.Bundle, FakeIssuer.AppleNonce(nonce), expires: DateTime.UtcNow.AddMinutes(-5));

        Assert.Contains("expired", (await SignInAsync("apple", token, nonce)).Detail);
    }

    [Fact]
    public async Task A_token_signed_by_someone_else_is_refused()
    {
        var nonce = await _host.NonceAsync();
        var forged = new FakeIssuer("https://appleid.apple.com").Token("apple-001", AuthHost.Bundle, FakeIssuer.AppleNonce(nonce));

        Assert.Contains("signature", (await SignInAsync("apple", forged, nonce)).Detail);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused()
    {
        var nonce = await _host.NonceAsync();
        var token = _host.AppleIssuer.Token("apple-001", AuthHost.Bundle, FakeIssuer.AppleNonce(nonce), issuer: "https://evil.example");

        Assert.Contains("issuer", (await SignInAsync("apple", token, nonce)).Detail);
    }

    [Fact]
    public async Task An_unknown_provider_is_404()
    {
        var (status, error, _) = await SignInAsync("facebook", "x", null);
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(SpineAuthErrors.UnknownProvider, error);
    }

    [Fact]
    public async Task Google_takes_the_nonce_as_it_is()
    {
        var nonce = await _host.NonceAsync();
        var token = _host.GoogleIssuer.Token("google-1", AuthHost.GoogleClient, nonce, "bo@example.com", true);

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync("google", token, nonce)).Status);
    }

    [Fact]
    public async Task Microsoft_accepts_a_token_without_a_nonce_from_an_accepted_tenant_only()
    {
        var work = _host.MicrosoftIssuer.Token("ms-1", AuthHost.MicrosoftClient, extra: new Dictionary<string, object> { ["tid"] = AuthHost.Tenant });
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync("microsoft", work, null)).Status);

        // "organizations" turns personal accounts away.
        var personal = _host.MicrosoftIssuer.Token("ms-2", AuthHost.MicrosoftClient,
            issuer: $"https://login.microsoftonline.com/{SpineMicrosoftOptions.ConsumersTenant}/v2.0",
            extra: new Dictionary<string, object> { ["tid"] = SpineMicrosoftOptions.ConsumersTenant });
        Assert.Contains("issuer", (await SignInAsync("microsoft", personal, null)).Detail);
    }

    [Fact]
    public async Task An_email_the_provider_does_not_vouch_for_is_not_kept()
    {
        // Anyone can put any email in their own Entra tenant; Microsoft's email is not trusted.
        var token = _host.MicrosoftIssuer.Token("ms-1", AuthHost.MicrosoftClient, email: "victim@example.com",
            extra: new Dictionary<string, object> { ["tid"] = AuthHost.Tenant });
        var response = await _host.PostAsync("/auth/microsoft/token", new SpineIdTokenRequest(token));
        var tokens = (await response.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;

        Assert.Null(tokens.User.Email);
        Assert.Null(await _host.Users.FindByEmailAsync("victim@example.com"));

        var nonce = await _host.NonceAsync();
        var unverified = _host.GoogleIssuer.Token("google-9", AuthHost.GoogleClient, nonce, "victim@example.com", false);
        var google = await _host.PostAsync("/auth/google/token", new SpineIdTokenRequest(unverified, nonce));
        Assert.Null((await google.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!.User.Email);
    }

    [Fact]
    public async Task A_long_name_is_cut()
    {
        var nonce = await _host.NonceAsync();
        var token = _host.AppleIssuer.Token("apple-long", AuthHost.Bundle, FakeIssuer.AppleNonce(nonce));
        var response = await _host.PostAsync("/auth/apple/token", new SpineIdTokenRequest(token, nonce, DisplayName: new string('A', 5000)));

        Assert.Equal(100, (await response.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!.User.DisplayName!.Length);
    }

    [Fact]
    public async Task Apple_keeps_its_refresh_token_from_the_authorization_code()
    {
        var tokens = await _host.SignInWithAppleAsync(code: "apple-code-1");

        var call = Assert.Single(_host.AppleEndpoints.Calls);
        Assert.Equal("/auth/token", call.Path);
        Assert.Equal("apple-code-1", call.Form["code"]);
        Assert.Equal(AuthHost.Bundle, call.Form["client_id"]);

        var secret = new JsonWebToken(call.Form["client_secret"]);
        Assert.Equal("TEAM123456", secret.Issuer);
        Assert.Equal(AuthHost.Bundle, secret.Subject);
        Assert.Equal("KEY1234567", secret.Kid);
        Assert.Equal("ES256", secret.Alg);
        Assert.Contains("https://appleid.apple.com", secret.Audiences);

        var login = Assert.Single(await _host.Users.GetLoginsAsync(tokens.User.Id));
        Assert.Equal("apple-refresh-1", login.ProviderRefreshToken);
    }

    [Fact]
    public async Task Without_linking_a_second_provider_with_the_same_email_is_a_second_user_without_it()
    {
        var apple = await _host.SignInWithAppleAsync(email: "same@example.com");

        var nonce = await _host.NonceAsync();
        var token = _host.GoogleIssuer.Token("google-1", AuthHost.GoogleClient, nonce, "same@example.com", true);
        var response = await _host.PostAsync("/auth/google/token", new SpineIdTokenRequest(token, nonce));
        var google = (await response.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;

        Assert.NotEqual(apple.User.Id, google.User.Id);
        Assert.Null(google.User.Email);
    }

    [Fact]
    public async Task The_jwks_has_the_key_that_signs_access_tokens()
    {
        var tokens = await _host.SignInWithAppleAsync();
        var jwks = JsonNode.Parse(await _host.Http.GetStringAsync("/auth/.well-known/jwks.json"))!;

        var key = Assert.Single(jwks["keys"]!.AsArray());
        Assert.Equal(new JsonWebToken(tokens.AccessToken).Kid, (string?)key!["kid"]);
        Assert.Equal("P-256", (string?)key["crv"]);
        Assert.Null(key["d"]);
    }
}

public sealed class LinkingTests
{
    [Fact]
    public async Task A_vouched_email_links_into_a_verified_account_only()
    {
        await using var host = await AuthHost.StartAsync(a => a.LinkByVerifiedEmail = true);

        var apple = await host.SignInWithAppleAsync(email: "anna@example.com");

        var nonce = await host.NonceAsync();
        var token = host.GoogleIssuer.Token("google-1", AuthHost.GoogleClient, nonce, "anna@example.com", true);
        var google = (await (await host.PostAsync("/auth/google/token", new SpineIdTokenRequest(token, nonce)))
            .Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;

        Assert.Equal(apple.User.Id, google.User.Id);
        Assert.Equal("google", google.User.Provider);
        Assert.Equal(2, (await host.Users.GetLoginsAsync(apple.User.Id)).Count);
    }

    [Fact]
    public async Task An_unverified_password_account_is_not_taken_over()
    {
        await using var host = await AuthHost.StartAsync(a => a.LinkByVerifiedEmail = true);

        // Someone signs up with Anna's email and never verifies it.
        Assert.Equal(HttpStatusCode.Accepted, (await host.PostAsync("/auth/password/signup", new SpinePasswordSignUp("anna@example.com", "hunter2hunter2"))).StatusCode);
        var squatter = await host.Users.FindByEmailAsync("anna@example.com");

        var apple = await host.SignInWithAppleAsync(email: "anna@example.com");

        Assert.NotEqual(squatter!.Id, apple.User.Id);
    }
}
