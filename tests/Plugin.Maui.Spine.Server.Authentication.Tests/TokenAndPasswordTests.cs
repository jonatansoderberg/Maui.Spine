using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Plugin.Maui.Spine.Server.Authentication.Tests;

public sealed class RefreshTests : IAsyncLifetime
{
    private AuthHost _host = null!;

    public async Task InitializeAsync() => _host = await AuthHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<HttpResponseMessage> RefreshAsync(string token) => _host.PostAsync("/auth/token", new SpineRefreshRequest(token));

    private static async Task<SpineTokens> TokensAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return (await response.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;
    }

    [Fact]
    public async Task A_refresh_token_gives_a_new_pair_and_keeps_the_provider()
    {
        var first = await _host.SignInWithAppleAsync();
        var second = await TokensAsync(await RefreshAsync(first.RefreshToken));

        Assert.Equal(first.User.Id, second.User.Id);
        Assert.Equal("apple", second.User.Provider);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, (await _host.WithBearerAsync(HttpMethod.Get, "/me", second.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task A_refresh_token_used_twice_revokes_the_whole_sign_in()
    {
        var first = await _host.SignInWithAppleAsync();
        var second = await TokensAsync(await RefreshAsync(first.RefreshToken));

        _host.Time.Advance(TimeSpan.FromSeconds(31));
        var replay = await RefreshAsync(first.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(SpineAuthErrors.InvalidGrant, (await AuthHost.ProblemAsync(replay))!.Error);

        // The honest device's newer token went with it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Another_sign_in_of_the_same_user_survives_a_replay()
    {
        var phone = await _host.SignInWithAppleAsync();
        var tablet = await _host.SignInWithAppleAsync();

        await RefreshAsync(phone.RefreshToken);
        _host.Time.Advance(TimeSpan.FromSeconds(31));
        await RefreshAsync(phone.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(tablet.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task A_client_whose_answer_was_lost_may_present_the_token_again_for_a_moment()
    {
        var first = await _host.SignInWithAppleAsync();
        var lost = await TokensAsync(await RefreshAsync(first.RefreshToken));

        _host.Time.Advance(TimeSpan.FromSeconds(10));
        var retried = await TokensAsync(await RefreshAsync(first.RefreshToken));

        // Both successors work: the one whose answer was lost and the one the client got.
        Assert.NotEqual(lost.RefreshToken, retried.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(retried.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Revoking_signs_that_device_out()
    {
        var tokens = await _host.SignInWithAppleAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _host.PostAsync("/auth/revoke", new SpineRefreshRequest(tokens.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _host.PostAsync("/auth/revoke", new SpineRefreshRequest("unknown"))).StatusCode);
    }

    [Fact]
    public async Task An_unused_refresh_token_expires()
    {
        var tokens = await _host.SignInWithAppleAsync();
        _host.Time.Advance(TimeSpan.FromDays(61));

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Garbage_is_invalid_grant_and_a_missing_body_is_400()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync("not-a-token")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.Http.PostAsync("/auth/token", new StringContent("{"))).StatusCode);
    }

    [Fact]
    public async Task Only_hashes_are_stored()
    {
        var tokens = await _host.SignInWithAppleAsync();
        Assert.DoesNotContain(_host.Store.RefreshTokens, t => t.Hash == tokens.RefreshToken);
    }

    [Fact]
    public async Task Deleting_the_account_revokes_apple_and_every_sign_in()
    {
        var phone = await _host.SignInWithAppleAsync(code: "apple-code-1");
        var tablet = await _host.SignInWithAppleAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Http.DeleteAsync("/auth/account")).StatusCode);

        var deleted = await _host.WithBearerAsync(HttpMethod.Delete, "/auth/account", phone.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal([phone.User.Id], _host.DeletedUsers);
        var revoke = Assert.Single(_host.AppleEndpoints.Calls, c => c.Path == "/auth/revoke");
        Assert.Equal("apple-refresh-1", revoke.Form["token"]);
        Assert.Equal("refresh_token", revoke.Form["token_type_hint"]);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tablet.RefreshToken)).StatusCode);
        Assert.Null(await _host.Users.FindByIdAsync(phone.User.Id));

        // The same Apple id signs up afresh.
        Assert.NotEqual(phone.User.Id, (await _host.SignInWithAppleAsync()).User.Id);
    }
}

public sealed class PasswordTests : IAsyncLifetime
{
    private const string Email = "Anna@Example.com";
    private const string Password = "correct horse";

    private AuthHost _host = null!;

    public async Task InitializeAsync() => _host = await AuthHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    // Emails go out after the answer, so each step waits for them before a test looks at what was sent.
    private async Task<HttpResponseMessage> Step(string action, object body)
    {
        var response = await _host.PostAsync($"/auth/password/{action}", body);
        await _host.App.Services.GetRequiredService<SpineAccounts>().EmailsSentAsync();
        return response;
    }

    private async Task<SpineTokens> SignedUpAsync()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Step("signup", new SpinePasswordSignUp(Email, Password, "Anna"))).StatusCode);
        var verified = await Step("verify", new SpineEmailCode(Email, _host.Email.LastCode));
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        return (await verified.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;
    }

    private async Task<string?> ErrorOf(HttpResponseMessage response) => (await AuthHost.ProblemAsync(response))?.Error;

    [Fact]
    public async Task Sign_up_sends_a_code_and_the_code_signs_in()
    {
        var tokens = await SignedUpAsync();

        var mail = _host.Email.Sent[0];
        Assert.Equal(SpineAuthEmailKind.VerifyEmail, mail.Kind);
        Assert.Equal(Email, mail.To);
        Assert.Equal(8, mail.Code!.Length);
        Assert.Equal("password", tokens.User.Provider);
        Assert.Equal("Anna", tokens.User.DisplayName);
    }

    [Fact]
    public async Task A_code_is_case_and_dash_tolerant_and_works_once()
    {
        await Step("signup", new SpinePasswordSignUp(Email, Password));
        var code = _host.Email.LastCode;
        var typed = $"{code[..4].ToLowerInvariant()}-{code[4..]}";

        Assert.Equal(HttpStatusCode.OK, (await Step("verify", new SpineEmailCode(Email.ToUpperInvariant(), typed))).StatusCode);

        var again = await Step("verify", new SpineEmailCode(Email, code));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(SpineAuthErrors.InvalidCode, await ErrorOf(again));
    }

    [Fact]
    public async Task An_unverified_account_cannot_sign_in_and_gets_a_new_code()
    {
        await Step("signup", new SpinePasswordSignUp(Email, Password));

        var refused = await Step("signin", new SpinePasswordSignIn(Email, Password));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(SpineAuthErrors.EmailNotVerified, await ErrorOf(refused));
        Assert.Equal(2, _host.Email.Sent.Count);
    }

    [Fact]
    public async Task A_verified_account_signs_in_with_its_password()
    {
        var up = await SignedUpAsync();

        var response = await Step("signin", new SpinePasswordSignIn("anna@example.com", Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(up.User.Id, (await response.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!.User.Id);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_answer_the_same()
    {
        await SignedUpAsync();

        var wrong = await Step("signin", new SpinePasswordSignIn(Email, "wrong password"));
        var unknown = await Step("signin", new SpinePasswordSignIn("nobody@example.com", Password));

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(SpineAuthErrors.InvalidCredentials, await ErrorOf(wrong));
        Assert.Equal(SpineAuthErrors.InvalidCredentials, await ErrorOf(unknown));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_a_while_without_saying_so()
    {
        await SignedUpAsync();

        for (var i = 0; i < 5; i++) await Step("signin", new SpinePasswordSignIn(Email, "wrong password"));

        var locked = await Step("signin", new SpinePasswordSignIn(Email, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(SpineAuthErrors.InvalidCredentials, await ErrorOf(locked));

        _host.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.OK, (await Step("signin", new SpinePasswordSignIn(Email, Password))).StatusCode);
    }

    [Fact]
    public async Task Parallel_wrong_passwords_are_all_counted()
    {
        await SignedUpAsync();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Step("signin", new SpinePasswordSignIn(Email, "wrong password"))));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Step("signin", new SpinePasswordSignIn(Email, Password))).StatusCode);
        var user = await _host.Users.FindByEmailAsync(Email);
        Assert.True(user!.LockedUntil > _host.Time.GetUtcNow());
    }

    [Fact]
    public async Task A_wrong_code_uses_the_code_up()
    {
        await Step("signup", new SpinePasswordSignUp(Email, Password));
        var code = _host.Email.LastCode;

        Assert.Equal(HttpStatusCode.BadRequest, (await Step("verify", new SpineEmailCode(Email, "AAAAAAAA"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Step("verify", new SpineEmailCode(Email, code))).StatusCode);
    }

    [Fact]
    public async Task A_new_code_replaces_the_last()
    {
        await SignedUpAsync();
        await Step("forgot", new SpineEmailRequest(Email));
        var first = _host.Email.LastCode;
        await Step("forgot", new SpineEmailRequest(Email));

        Assert.Equal(HttpStatusCode.BadRequest, (await Step("reset", new SpinePasswordReset(Email, first, "a new password"))).StatusCode);
    }

    [Fact]
    public async Task A_huge_password_is_refused_before_hashing()
    {
        await SignedUpAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Step("signin", new SpinePasswordSignIn(Email, new string('x', 100_000)))).StatusCode);
    }

    [Fact]
    public async Task Signing_up_again_tells_nobody_but_the_owner()
    {
        await SignedUpAsync();

        var again = await Step("signup", new SpinePasswordSignUp(Email, "another password"));

        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(SpineAuthEmailKind.AlreadyRegistered, _host.Email.Sent.Last().Kind);
        Assert.Equal(HttpStatusCode.OK, (await Step("signin", new SpinePasswordSignIn(Email, Password))).StatusCode);
    }

    [Fact]
    public async Task A_reset_sets_a_new_password_and_signs_out_every_old_session()
    {
        var old = await SignedUpAsync();

        Assert.Equal(HttpStatusCode.Accepted, (await Step("forgot", new SpineEmailRequest(Email))).StatusCode);
        Assert.Equal(SpineAuthEmailKind.ResetPassword, _host.Email.Sent.Last().Kind);

        var reset = await Step("reset", new SpinePasswordReset(Email, _host.Email.LastCode, "a new password"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/auth/token", new SpineRefreshRequest(old.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Step("signin", new SpinePasswordSignIn(Email, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Step("signin", new SpinePasswordSignIn(Email, "a new password"))).StatusCode);
    }

    [Fact]
    public async Task Forgot_for_an_unknown_email_answers_the_same_and_sends_nothing()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Step("forgot", new SpineEmailRequest("nobody@example.com"))).StatusCode);
        Assert.Empty(_host.Email.Sent);
    }

    [Fact]
    public async Task A_reset_code_is_not_a_verify_code()
    {
        await Step("signup", new SpinePasswordSignUp(Email, Password));
        await Step("forgot", new SpineEmailRequest(Email));

        var response = await Step("verify", new SpineEmailCode(Email, _host.Email.LastCode));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_code_expires()
    {
        await Step("signup", new SpinePasswordSignUp(Email, Password));
        _host.Time.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.BadRequest, (await Step("verify", new SpineEmailCode(Email, _host.Email.LastCode))).StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", "long enough", SpineAuthErrors.InvalidRequest)]
    [InlineData("anna@example.com", "short", SpineAuthErrors.WeakPassword)]
    public async Task Bad_sign_ups_are_400(string email, string password, string error)
    {
        var response = await Step("signup", new SpinePasswordSignUp(email, password));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await ErrorOf(response));
    }

    [Fact]
    public async Task An_unknown_step_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Step("hack", new SpineEmailRequest(Email))).StatusCode);
    }

    [Fact]
    public async Task An_apple_user_can_add_a_password_through_a_reset()
    {
        var apple = await _host.SignInWithAppleAsync(email: "anna@example.com");

        await Step("forgot", new SpineEmailRequest("anna@example.com"));
        var reset = await Step("reset", new SpinePasswordReset("anna@example.com", _host.Email.LastCode, "a new password"));

        Assert.Equal(apple.User.Id, (await reset.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!.User.Id);
        Assert.Equal(HttpStatusCode.OK, (await Step("signin", new SpinePasswordSignIn("anna@example.com", "a new password"))).StatusCode);
    }
}

public sealed class SetupTests
{
    private static string Key() => ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();

    [Fact]
    public void A_missing_signing_key_says_how_to_make_one()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.EmailPassword();
            a.UseInMemoryStore();
        }));

        Assert.Contains("openssl", e.Message);
    }

    [Fact]
    public void A_provider_without_client_ids_is_refused()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = Key();
            a.Google(_ => { });
            a.UseInMemoryStore();
        }));

        Assert.Contains("ClientIds", e.Message);
    }

    [Fact]
    public void A_wrong_kind_of_key_is_refused_with_its_name()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP384).ExportPkcs8PrivateKeyPem();
            a.EmailPassword();
            a.UseInMemoryStore();
        });

        var e = Assert.Throws<InvalidOperationException>(() => services.BuildServiceProvider().GetRequiredService<SpineTokenService>());
        Assert.Contains("P-256", e.Message);
    }

    [Fact]
    public async Task Email_and_password_without_a_sender_stops_the_host()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = Key();
            a.EmailPassword();
            a.UseInMemoryStore();
        });

        await using var app = builder.Build();
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
        Assert.Contains(nameof(ISpineAuthEmailSender), e.Message);
    }

    [Fact]
    public void A_custom_provider_name_must_fit_a_route()
    {
        Assert.Throws<ArgumentException>(() => new SpineAuthOptions().OpenIdConnect("Corp/Login", _ => { }));
        Assert.Throws<ArgumentException>(() => new SpineAuthOptions().OpenIdConnect("password", _ => { }));
    }
}
