using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Plugin.Maui.Spine.Server.Authentication.Tests;

/// <summary>Passkeys: sign-up, sign-in, a passkey added to an account, and the association files (#522).</summary>
public sealed class PasskeyTests : IAsyncLifetime
{
    private AuthHost _host = null!;
    private readonly SoftwareAuthenticator _iPhone = new($"https://{AuthHost.PasskeyDomain}", AuthHost.PasskeyDomain);

    public async Task InitializeAsync() => _host = await AuthHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpResponseMessage> SendAsync(string path, object? body, string? accessToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: SpineAuthJson.Options);
        return await _host.Http.SendAsync(request);
    }

    private async Task<SpinePasskeyChallenge> ChallengeAsync(string path, object? body, string? accessToken = null)
    {
        var response = await SendAsync(path, body, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SpinePasskeyChallenge>(SpineAuthJson.Options))!;
    }

    private static async Task<SpineTokens> TokensAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;
    }

    private async Task<SpineTokens> SignUpAsync(string? name = "Bo", string? email = "bo@example.com")
    {
        var challenge = await ChallengeAsync("/auth/passkey/register/options", new SpinePasskeyNewAccount(name, email));
        return await TokensAsync(await SendAsync("/auth/passkey/register",
            new SpinePasskeyResponse(challenge.ChallengeId, _iPhone.Create(challenge.Options))));
    }

    private async Task<HttpResponseMessage> SignInAsync(string? id = null, byte[]? userHandle = null)
    {
        var challenge = await ChallengeAsync("/auth/passkey/signin/options", null);
        return await SendAsync("/auth/passkey/signin", new SpinePasskeyResponse(challenge.ChallengeId, _iPhone.Get(challenge.Options, id, userHandle)));
    }

    [Fact]
    public async Task A_passkey_makes_an_account_that_signs_in_with_it()
    {
        var signedUp = await SignUpAsync();

        Assert.Equal("passkey", signedUp.User.Provider);
        Assert.Equal("Bo", signedUp.User.DisplayName);

        var signedIn = await TokensAsync(await SignInAsync());
        Assert.Equal(signedUp.User.Id, signedIn.User.Id);
        Assert.Equal("passkey", signedIn.User.Provider);
    }

    [Fact]
    public async Task The_email_given_at_sign_up_stays_unverified_and_off_the_account()
    {
        var signedUp = await SignUpAsync(email: "anna@example.com");

        Assert.Null(signedUp.User.Email);
        Assert.Null((await _host.Users.FindByIdAsync(signedUp.User.Id))!.Email);
        Assert.Null(await _host.Users.FindByEmailAsync("anna@example.com"));
    }

    [Fact]
    public async Task The_options_ask_for_a_discoverable_passkey_with_user_verification()
    {
        var challenge = await ChallengeAsync("/auth/passkey/register/options", new SpinePasskeyNewAccount("Bo", null));
        var options = JsonNode.Parse(challenge.Options)!;

        Assert.Equal(AuthHost.PasskeyDomain, (string)options["rp"]!["id"]!);
        Assert.Equal("required", (string)options["authenticatorSelection"]!["residentKey"]!);
        Assert.Equal("required", (string)options["authenticatorSelection"]!["userVerification"]!);
        Assert.Equal("Bo", (string)options["user"]!["name"]!);
    }

    [Fact]
    public async Task A_new_account_needs_a_name_or_an_email()
    {
        var response = await SendAsync("/auth/passkey/register/options", new SpinePasskeyNewAccount(" ", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sign_up_can_be_closed()
    {
        await _host.DisposeAsync();
        _host = await AuthHost.StartAsync(a => a.Passkeys(p =>
        {
            p.RelyingPartyId = AuthHost.PasskeyDomain;
            p.AllowSignUp = false;
        }));

        var response = await SendAsync("/auth/passkey/register/options", new SpinePasskeyNewAccount("Bo", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SpineAuthErrors.SignUpClosed, (await AuthHost.ProblemAsync(response))!.Error);
    }

    [Fact]
    public async Task A_signed_in_user_adds_a_passkey_and_then_signs_in_with_it()
    {
        var apple = await _host.SignInWithAppleAsync();

        var challenge = await ChallengeAsync("/auth/passkey/register/options", null, apple.AccessToken);
        var added = await TokensAsync(await SendAsync("/auth/passkey/register",
            new SpinePasskeyResponse(challenge.ChallengeId, _iPhone.Create(challenge.Options))));

        Assert.Equal(apple.User.Id, added.User.Id);
        Assert.Equal("apple", added.User.Provider);
        Assert.Equal(["apple", "passkey"], added.User.Logins!.Select(l => l.Provider).Order().ToArray());

        var signedIn = await TokensAsync(await SignInAsync());
        Assert.Equal(apple.User.Id, signedIn.User.Id);
    }

    [Fact]
    public async Task Adding_a_passkey_excludes_the_ones_the_account_has()
    {
        var signedUp = await SignUpAsync();

        var challenge = await ChallengeAsync("/auth/passkey/register/options", null, signedUp.AccessToken);
        var excluded = JsonNode.Parse(challenge.Options)!["excludeCredentials"]!.AsArray().Select(c => (string)c!["id"]!).ToArray();

        Assert.Equal(_iPhone.Ids.ToArray(), excluded);
    }

    [Fact]
    public async Task A_bad_access_token_does_not_fall_back_to_a_new_account()
    {
        var response = await SendAsync("/auth/passkey/register/options", new SpinePasskeyNewAccount("Bo", null), "not-a-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_ceremony_works_once()
    {
        var challenge = await ChallengeAsync("/auth/passkey/register/options", new SpinePasskeyNewAccount("Bo", null));
        var credential = _iPhone.Create(challenge.Options);
        await TokensAsync(await SendAsync("/auth/passkey/register", new SpinePasskeyResponse(challenge.ChallengeId, credential)));

        var again = await SendAsync("/auth/passkey/register", new SpinePasskeyResponse(challenge.ChallengeId, credential));

        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Equal(SpineAuthErrors.InvalidGrant, (await AuthHost.ProblemAsync(again))!.Error);
    }

    [Fact]
    public async Task A_sign_in_challenge_cannot_finish_a_registration()
    {
        var challenge = await ChallengeAsync("/auth/passkey/signin/options", null);
        var options = JsonNode.Parse(challenge.Options)!;
        options["user"] = new JsonObject { ["id"] = Base64UrlEncoder.Encode("someone"u8.ToArray()) };

        var response = await SendAsync("/auth/passkey/register", new SpinePasskeyResponse(challenge.ChallengeId, _iPhone.Create(options.ToJsonString())));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_passkey_made_for_another_site_is_refused()
    {
        var challenge = await ChallengeAsync("/auth/passkey/register/options", new SpinePasskeyNewAccount("Bo", null));

        var response = await SendAsync("/auth/passkey/register",
            new SpinePasskeyResponse(challenge.ChallengeId, _iPhone.Create(challenge.Options, originOverride: "https://evil.test")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpineAuthErrors.InvalidToken, (await AuthHost.ProblemAsync(response))!.Error);
    }

    [Fact]
    public async Task A_sign_in_whose_user_handle_is_someone_else_is_refused()
    {
        await SignUpAsync();

        var response = await SignInAsync(userHandle: "someone-else"u8.ToArray());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpineAuthErrors.InvalidToken, (await AuthHost.ProblemAsync(response))!.Error);
    }

    [Fact]
    public async Task An_unknown_passkey_is_refused()
    {
        var signedUp = await SignUpAsync();
        var login = (await _host.Users.GetLoginsAsync(signedUp.User.Id)).Single();
        await _host.Users.DeleteLoginAsync(login);

        var response = await SignInAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpineAuthErrors.UnknownPasskey, (await AuthHost.ProblemAsync(response))!.Error);
    }

    [Fact]
    public async Task A_signature_counter_is_kept_and_must_move_forward()
    {
        _iPhone.KeepsCounter = true;
        var signedUp = await SignUpAsync();

        await TokensAsync(await SignInAsync());
        await TokensAsync(await SignInAsync());
        Assert.Equal(2u, (await _host.Users.GetLoginsAsync(signedUp.User.Id)).Single().PasskeySignCount);

        // A cloned authenticator answers with a counter the server has already seen.
        var login = (await _host.Users.GetLoginsAsync(signedUp.User.Id)).Single();
        await _host.Users.SaveLoginAsync(login with { PasskeySignCount = 10 });

        var response = await SignInAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_last_passkey_cannot_be_removed()
    {
        var signedUp = await SignUpAsync();
        using var unlink = new HttpRequestMessage(HttpMethod.Delete, "/auth/link/passkey");
        unlink.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedUp.AccessToken);

        var response = await _host.Http.SendAsync(unlink);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SpineAuthErrors.LastLogin, (await AuthHost.ProblemAsync(response))!.Error);
    }

    [Fact]
    public async Task The_association_files_name_the_apps()
    {
        var apple = JsonNode.Parse(await _host.Http.GetStringAsync("/.well-known/apple-app-site-association"))!;
        Assert.Equal($"TEAM123456.{AuthHost.Bundle}", (string)apple["webcredentials"]!["apps"]![0]!);

        var android = JsonNode.Parse(await _host.Http.GetStringAsync("/.well-known/assetlinks.json"))!.AsArray().Single()!;
        Assert.Equal("delegate_permission/common.get_login_creds", (string)android["relation"]![0]!);
        Assert.Equal(AuthHost.Bundle, (string)android["target"]!["package_name"]!);
        Assert.Equal(AuthHost.AndroidFingerprint, (string)android["target"]!["sha256_cert_fingerprints"]![0]!);
    }

    [Fact]
    public async Task An_android_app_signs_in_with_its_certificate_as_the_origin()
    {
        var hash = Base64UrlEncoder.Encode(Convert.FromHexString(AuthHost.AndroidFingerprint.Replace(":", "")));
        var android = new SoftwareAuthenticator($"android:apk-key-hash:{hash}", AuthHost.PasskeyDomain);

        var challenge = await ChallengeAsync("/auth/passkey/register/options", new SpinePasskeyNewAccount("Bo", null));
        var response = await SendAsync("/auth/passkey/register", new SpinePasskeyResponse(challenge.ChallengeId, android.Create(challenge.Options)));

        await TokensAsync(response);
    }

    [Fact]
    public async Task Without_passkeys_the_endpoints_are_not_there()
    {
        await _host.DisposeAsync();
        _host = await AuthHost.StartAsync(passkeys: false);

        var response = await SendAsync("/auth/passkey/signin/options", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.Http.GetAsync("/.well-known/apple-app-site-association")).StatusCode);
    }
}
