using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Plugin.Maui.Spine.Server.Authentication.Tests;

/// <summary>One account, several ways to sign in: linking, unlinking and the pending identity (#575).</summary>
public sealed class AccountLinkingTests : IAsyncLifetime
{
    private const string AppRedirect = "se.example.app:/signed-in";
    private const string Password = "correct horse battery";

    private AuthHost _host = null!;
    private readonly string _verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public async Task InitializeAsync() => _host = await AuthHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task AskBeforeNewAccountAsync(bool linkByVerifiedEmail = false)
    {
        await _host.DisposeAsync();
        _host = await AuthHost.StartAsync(a =>
        {
            a.AskBeforeNewAccount = true;
            a.LinkByVerifiedEmail = linkByVerifiedEmail;
        });
    }

    private async Task<HttpResponseMessage> GoogleAsync(string subject = "google-1", string? email = "bo@example.com")
    {
        var nonce = await _host.NonceAsync();
        var token = _host.GoogleIssuer.Token(subject, AuthHost.GoogleClient, nonce, email, email is null ? null : true);
        return await _host.PostAsync("/auth/google/token", new SpineIdTokenRequest(token, nonce));
    }

    private async Task<SpineIdTokenRequest> GoogleRequestAsync(string subject = "google-1")
    {
        var nonce = await _host.NonceAsync();
        return new SpineIdTokenRequest(_host.GoogleIssuer.Token(subject, AuthHost.GoogleClient, nonce, "bo@example.com", true), nonce);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: SpineAuthJson.Options);
        return await _host.Http.SendAsync(request);
    }

    private static async Task<SpineTokens> TokensAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;
    }

    private static async Task<SpinePendingIdentity> PendingAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SpinePendingIdentity>(SpineAuthJson.Options))!;
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response) => (await AuthHost.ProblemAsync(response))?.Error;

    private static string[] Providers(SpineTokens tokens) => tokens.User.Logins!.Select(l => l.Provider).Order().ToArray();

    [Fact]
    public async Task A_google_id_token_joins_the_signed_in_user_and_then_signs_in_to_it()
    {
        var apple = await _host.SignInWithAppleAsync();

        var linked = await TokensAsync(await SendAsync(HttpMethod.Post, "/auth/link/google", apple.AccessToken, await GoogleRequestAsync()));

        Assert.Equal(apple.User.Id, linked.User.Id);
        Assert.Equal("apple", linked.User.Provider);
        Assert.Equal(["apple", "google"], Providers(linked));
        Assert.Equal(["apple", "google"], linked.User.SignInMethods!.Order().ToArray());
        var google = linked.User.Logins!.Single(l => l.Provider == "google");
        Assert.Equal("bo@example.com", google.Email);
        Assert.NotNull(google.LinkedAt);

        var signedIn = await TokensAsync(await GoogleAsync());
        Assert.Equal(apple.User.Id, signedIn.User.Id);
        Assert.Equal("google", signedIn.User.Provider);
    }

    [Fact]
    public async Task An_identity_another_account_signs_in_with_is_refused_with_a_reason()
    {
        var other = await TokensAsync(await GoogleAsync());
        var apple = await _host.SignInWithAppleAsync(email: "anna@example.com");
        Assert.NotEqual(other.User.Id, apple.User.Id);

        var response = await SendAsync(HttpMethod.Post, "/auth/link/google", apple.AccessToken, await GoogleRequestAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await AuthHost.ProblemAsync(response);
        Assert.Equal(SpineAuthErrors.IdentityInUse, problem!.Error);
        Assert.Contains("another account", problem.Detail);
    }

    [Fact]
    public async Task Linking_an_identity_the_user_already_has_changes_nothing()
    {
        var apple = await _host.SignInWithAppleAsync();
        await TokensAsync(await SendAsync(HttpMethod.Post, "/auth/link/google", apple.AccessToken, await GoogleRequestAsync()));

        var again = await TokensAsync(await SendAsync(HttpMethod.Post, "/auth/link/google", apple.AccessToken, await GoogleRequestAsync()));

        Assert.Equal(["apple", "google"], Providers(again));
    }

    [Fact]
    public async Task Linking_needs_the_users_access_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Post, "/auth/link/google", null, await GoogleRequestAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Post, "/auth/link/ticket", "not-a-token")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Delete, "/auth/link/google", null)).StatusCode);
    }

    [Fact]
    public async Task An_unlinked_identity_no_longer_signs_in_to_the_account()
    {
        var apple = await _host.SignInWithAppleAsync();
        await TokensAsync(await SendAsync(HttpMethod.Post, "/auth/link/google", apple.AccessToken, await GoogleRequestAsync()));

        var unlinked = await TokensAsync(await SendAsync(HttpMethod.Delete, "/auth/link/google", apple.AccessToken));

        Assert.Equal(["apple"], Providers(unlinked));
        var google = await TokensAsync(await GoogleAsync(email: null));
        Assert.NotEqual(apple.User.Id, google.User.Id);
    }

    [Fact]
    public async Task The_last_way_to_sign_in_is_never_removed()
    {
        var apple = await _host.SignInWithAppleAsync();

        var last = await SendAsync(HttpMethod.Delete, "/auth/link/apple", apple.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, last.StatusCode);
        Assert.Equal(SpineAuthErrors.LastLogin, await ErrorAsync(last));

        var missing = await SendAsync(HttpMethod.Delete, "/auth/link/microsoft", apple.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Every_sign_in_keeps_when_its_login_was_last_used_but_a_link_does_not()
    {
        const string email = "dag@example.com";
        await _host.PostAsync("/auth/password/signup", new SpinePasswordSignUp(email, Password));
        var verified = await TokensAsync(await _host.PostAsync("/auth/password/verify", new SpineEmailCode(email, _host.Email.LastCode)));
        var start = _host.Time.GetUtcNow();
        Assert.Equal(start, verified.User.Logins!.Single().LastUsedAt);

        // Linked before the clock moves: the access token is checked against the real clock.
        var linked = await TokensAsync(await SendAsync(HttpMethod.Post, "/auth/link/google", verified.AccessToken, await GoogleRequestAsync()));
        Assert.Null(linked.User.Logins!.Single(l => l.Provider == "google").LastUsedAt);

        _host.Time.Advance(TimeSpan.FromMinutes(1));
        var signedIn = await TokensAsync(await _host.PostAsync("/auth/password/signin", new SpinePasswordSignIn(email, Password)));
        Assert.Equal(_host.Time.GetUtcNow(), signedIn.User.Logins!.Single(l => l.Provider == "password").LastUsedAt);

        _host.Time.Advance(TimeSpan.FromMinutes(1));
        var withGoogle = await TokensAsync(await GoogleAsync());
        var google = withGoogle.User.Logins!.Single(l => l.Provider == "google");
        Assert.Equal(start, google.LinkedAt);
        Assert.Equal(_host.Time.GetUtcNow(), google.LastUsedAt);
        Assert.Equal(_host.Time.GetUtcNow() - TimeSpan.FromMinutes(1), withGoogle.User.Logins!.Single(l => l.Provider == "password").LastUsedAt);
    }

    [Fact]
    public async Task Removing_the_password_stops_it_signing_in()
    {
        const string email = "cia@example.com";
        Assert.Equal(HttpStatusCode.Accepted, (await _host.PostAsync("/auth/password/signup", new SpinePasswordSignUp(email, Password))).StatusCode);
        var password = await TokensAsync(await _host.PostAsync("/auth/password/verify", new SpineEmailCode(email, _host.Email.LastCode)));
        Assert.Equal("password", Assert.Single(password.User.Logins!).Provider);
        Assert.Equal(email, password.User.Logins![0].Email);

        await TokensAsync(await SendAsync(HttpMethod.Post, "/auth/link/google", password.AccessToken, await GoogleRequestAsync()));
        var unlinked = await TokensAsync(await SendAsync(HttpMethod.Delete, "/auth/link/password", password.AccessToken));

        Assert.Equal(["google"], Providers(unlinked));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/auth/password/signin", new SpinePasswordSignIn(email, Password))).StatusCode);
    }

    private async Task<(HttpResponseMessage Start, Dictionary<string, string> Provider)> StartGoogleAsync(string? link, string subject = "google-1")
    {
        var query = new Dictionary<string, string?>
        {
            ["redirect_uri"] = AppRedirect,
            ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(_verifier))),
            ["code_challenge_method"] = "S256",
            ["state"] = "app-1",
        };
        if (link is not null) query["link"] = link;

        var start = await _host.Http.GetAsync(QueryHelpers.AddQueryString("/auth/google/start", query));
        if (start.StatusCode != HttpStatusCode.Redirect) return (start, []);

        var provider = QueryHelpers.ParseQuery(start.Headers.Location!.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
        _host.AppleEndpoints.TokenEndpoint = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id_token = _host.GoogleIssuer.Token(subject, AuthHost.GoogleWeb, provider["nonce"], "bo@example.com", true), access_token = "x" }),
        };
        return (start, provider);
    }

    private async Task<HttpResponseMessage> BrowserGoogleAsync(string? link = null)
    {
        var (_, provider) = await StartGoogleAsync(link);
        var callback = await _host.Http.GetAsync($"/auth/google/callback?code=provider-code&state={provider["state"]}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var app = QueryHelpers.ParseQuery(new Uri(callback.Headers.Location!.OriginalString).Query);
        return await _host.PostAsync("/auth/token", new SpineCodeExchange(app["code"].ToString(), _verifier, AppRedirect));
    }

    [Fact]
    public async Task A_link_ticket_makes_a_browser_sign_in_join_the_signed_in_user()
    {
        var apple = await _host.SignInWithAppleAsync();
        var ticket = (await (await SendAsync(HttpMethod.Post, "/auth/link/ticket", apple.AccessToken)).Content.ReadFromJsonAsync<SpineLinkTicket>(SpineAuthJson.Options))!;

        var linked = await TokensAsync(await BrowserGoogleAsync(ticket.Ticket));

        Assert.Equal(apple.User.Id, linked.User.Id);
        Assert.Equal("apple", linked.User.Provider);
        Assert.Equal(["apple", "google"], Providers(linked));
    }

    [Fact]
    public async Task A_browser_link_happens_only_at_the_code_exchange()
    {
        // Someone else opening a start URL with this user's ticket goes through Google and the callback, but never has
        // the PKCE verifier, so the Google account they signed in with is not linked.
        var apple = await _host.SignInWithAppleAsync();
        var ticket = (await (await SendAsync(HttpMethod.Post, "/auth/link/ticket", apple.AccessToken)).Content.ReadFromJsonAsync<SpineLinkTicket>(SpineAuthJson.Options))!;
        var (_, provider) = await StartGoogleAsync(ticket.Ticket);

        var callback = await _host.Http.GetAsync($"/auth/google/callback?code=provider-code&state={provider["state"]}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Null(await _host.Users.FindByLoginAsync("google", "google-1"));
        var code = QueryHelpers.ParseQuery(new Uri(callback.Headers.Location!.OriginalString).Query)["code"].ToString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/auth/token", new SpineCodeExchange(code, "not-the-verifier", AppRedirect))).StatusCode);
        Assert.Null(await _host.Users.FindByLoginAsync("google", "google-1"));
    }

    [Fact]
    public async Task A_browser_link_to_an_identity_in_use_is_refused_at_the_exchange()
    {
        await TokensAsync(await GoogleAsync());
        var apple = await _host.SignInWithAppleAsync(email: "anna@example.com");
        var ticket = (await (await SendAsync(HttpMethod.Post, "/auth/link/ticket", apple.AccessToken)).Content.ReadFromJsonAsync<SpineLinkTicket>(SpineAuthJson.Options))!;

        var response = await BrowserGoogleAsync(ticket.Ticket);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SpineAuthErrors.IdentityInUse, await ErrorAsync(response));
    }

    [Fact]
    public async Task A_link_ticket_works_once()
    {
        var apple = await _host.SignInWithAppleAsync();
        var ticket = (await (await SendAsync(HttpMethod.Post, "/auth/link/ticket", apple.AccessToken)).Content.ReadFromJsonAsync<SpineLinkTicket>(SpineAuthJson.Options))!;
        await StartGoogleAsync(ticket.Ticket);

        var (again, _) = await StartGoogleAsync(ticket.Ticket);

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task With_ask_before_new_account_an_unknown_identity_waits_and_makes_no_user()
    {
        await AskBeforeNewAccountAsync();

        var pending = await PendingAsync(await GoogleAsync());

        Assert.Equal("google", pending.Provider);
        Assert.Equal("bo@example.com", pending.Email);
        Assert.True(pending.ExpiresAt > _host.Time.GetUtcNow().AddMinutes(9));
        Assert.Null(await _host.Users.FindByLoginAsync("google", "google-1"));

        var created = await TokensAsync(await _host.PostAsync("/auth/pending/create", new SpinePendingRequest(pending.Ticket)));
        Assert.Equal("google", created.User.Provider);
        Assert.Equal(["google"], Providers(created));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/auth/pending/create", new SpinePendingRequest(pending.Ticket))).StatusCode);
        Assert.Equal(created.User.Id, (await TokensAsync(await GoogleAsync())).User.Id);
    }

    [Fact]
    public async Task A_pending_identity_joins_the_account_the_user_proves()
    {
        await AskBeforeNewAccountAsync();
        var applePending = await PendingAsync(await PostAppleAsync());
        var apple = await TokensAsync(await _host.PostAsync("/auth/pending/create", new SpinePendingRequest(applePending.Ticket)));

        var googlePending = await PendingAsync(await GoogleAsync(email: null));
        var proved = await TokensAsync(await PostAppleAsync());
        var linked = await TokensAsync(await SendAsync(HttpMethod.Post, "/auth/pending/link", proved.AccessToken, new SpinePendingRequest(googlePending.Ticket)));

        Assert.Equal(apple.User.Id, linked.User.Id);
        Assert.Equal(["apple", "google"], Providers(linked));
        Assert.Equal(apple.User.Id, (await TokensAsync(await GoogleAsync(email: null))).User.Id);
    }

    private async Task<HttpResponseMessage> PostAppleAsync()
    {
        var nonce = await _host.NonceAsync();
        var token = _host.AppleIssuer.Token("apple-001", AuthHost.Bundle, FakeIssuer.AppleNonce(nonce), "anna@example.com", "true");
        return await _host.PostAsync("/auth/apple/token", new SpineIdTokenRequest(token, nonce));
    }

    [Fact]
    public async Task A_pending_identity_cannot_join_an_account_that_another_user_has_it_on()
    {
        await AskBeforeNewAccountAsync();
        var first = await PendingAsync(await GoogleAsync(email: null));
        var second = await PendingAsync(await GoogleAsync(email: null));
        await TokensAsync(await _host.PostAsync("/auth/pending/create", new SpinePendingRequest(first.Ticket)));
        var apple = await TokensAsync(await _host.PostAsync("/auth/pending/create", new SpinePendingRequest((await PendingAsync(await PostAppleAsync())).Ticket)));

        var response = await SendAsync(HttpMethod.Post, "/auth/pending/link", apple.AccessToken, new SpinePendingRequest(second.Ticket));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SpineAuthErrors.IdentityInUse, await ErrorAsync(response));
    }

    [Fact]
    public async Task A_pending_identity_expires_and_linking_it_needs_an_access_token()
    {
        await AskBeforeNewAccountAsync();
        var pending = await PendingAsync(await GoogleAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/auth/pending/link", new SpinePendingRequest(pending.Ticket))).StatusCode);

        _host.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/auth/pending/create", new SpinePendingRequest(pending.Ticket))).StatusCode);
    }

    [Fact]
    public async Task A_browser_sign_in_can_end_pending_too()
    {
        await AskBeforeNewAccountAsync();

        var pending = await PendingAsync(await BrowserGoogleAsync());

        Assert.Equal("google", pending.Provider);
        var created = await TokensAsync(await _host.PostAsync("/auth/pending/create", new SpinePendingRequest(pending.Ticket)));
        Assert.Equal(["google"], Providers(created));
    }

    [Fact]
    public async Task A_verified_email_still_links_without_asking()
    {
        await AskBeforeNewAccountAsync(linkByVerifiedEmail: true);
        await _host.PostAsync("/auth/password/signup", new SpinePasswordSignUp("bo@example.com", Password));
        var password = await TokensAsync(await _host.PostAsync("/auth/password/verify", new SpineEmailCode("bo@example.com", _host.Email.LastCode)));

        var google = await TokensAsync(await GoogleAsync());

        Assert.Equal(password.User.Id, google.User.Id);
        Assert.Equal(["google", "password"], Providers(google));
    }

    [Fact]
    public async Task The_logins_come_with_every_refresh()
    {
        var apple = await _host.SignInWithAppleAsync();
        await TokensAsync(await SendAsync(HttpMethod.Post, "/auth/link/google", apple.AccessToken, await GoogleRequestAsync()));

        var refreshed = await TokensAsync(await _host.PostAsync("/auth/token", new SpineRefreshRequest(apple.RefreshToken)));

        Assert.Equal(["apple", "google"], Providers(refreshed));
    }
}
