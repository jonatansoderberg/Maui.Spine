using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Plugin.Maui.Spine.Authentication.Tests;

/// <summary>The mode without a Spine server through <see cref="IDirectSignIn"/>, as MSAL's <c>UseMicrosoft</c> plugs in.</summary>
public sealed class DirectTests
{
    private static readonly SpineUser Anna = new("oid-1", "anna@contoso.com", "Anna", "microsoft");

    private sealed class FakeMsal : IDirectSignIn
    {
        public int Status { get; set; } = 200;
        public string? Removed { get; private set; }
        public int Silent { get; private set; }

        public Task<(SpineTokens? Tokens, SignInResult? Problem)> SignInAsync(CancellationToken cancellationToken) =>
            Task.FromResult<(SpineTokens?, SignInResult?)>((Tokens("at-1", DateTimeOffset.UtcNow), null));

        public Task<ServerAnswer> RefreshAsync(string refreshToken, SpineUser? user, CancellationToken cancellationToken)
        {
            Silent++;
            return Task.FromResult(Status == 200
                ? new ServerAnswer(Tokens($"at-{Silent + 1}", DateTimeOffset.UtcNow.AddHours(1)), 200, null)
                : new ServerAnswer(null, Status, "invalid_grant"));
        }

        public Task<ServerAnswer> RevokeAsync(string refreshToken, CancellationToken cancellationToken)
        {
            Removed = refreshToken;
            return Task.FromResult(new ServerAnswer(null, 200, null));
        }

        // The "refresh token" is MSAL's account id: the session hands it back on every refresh and at sign-out.
        private static SpineTokens Tokens(string access, DateTimeOffset expires) => new(access, expires, "account-1", Anna);
    }

    private static async Task<(FakeMsal Msal, SpineAuthSession Session)> SignedInAsync()
    {
        var msal = new FakeMsal();
        var session = new SpineAuthSession(msal, new InMemorySpineAuthStorage(), TimeProvider.System, NullLogger.Instance);
        var (tokens, _) = await msal.SignInAsync(default);
        await session.AcceptAsync(tokens!);
        return (msal, session);
    }

    [Fact]
    public async Task An_expired_token_is_renewed_silently_with_the_account_id()
    {
        var (msal, session) = await SignedInAsync();

        Assert.Equal("at-2", await session.GetAccessTokenAsync());
        Assert.Equal(1, msal.Silent);
        Assert.Equal("at-2", await session.GetAccessTokenAsync());
        Assert.Equal(1, msal.Silent);
    }

    [Fact]
    public async Task A_sign_in_msal_needs_again_signs_out()
    {
        var (msal, session) = await SignedInAsync();
        msal.Status = 401;

        Assert.Null(await session.GetAccessTokenAsync());
        Assert.Null(session.User);
    }

    [Fact]
    public async Task Offline_keeps_the_session()
    {
        var (msal, session) = await SignedInAsync();
        msal.Status = 0;

        Assert.Null(await session.GetAccessTokenAsync());
        Assert.Equal("oid-1", session.User!.Id);
    }

    [Fact]
    public async Task Sign_out_removes_the_account()
    {
        var (msal, session) = await SignedInAsync();

        await session.SignOutAsync();

        Assert.Equal("account-1", msal.Removed);
    }

    private static DirectProvider Msal(string method = "UseMicrosoft(...)") => new("microsoft", method, UsesBrowser: false, _ => new FakeMsal());

    [Fact]
    public void A_direct_provider_needs_no_server_and_takes_none()
    {
        new SpineAuthenticationOptions().UseDirect(Msal()).Validate(returnsToScheme: false, appStore: true);

        var withServer = new SpineAuthenticationOptions { Server = new Uri("https://api.test/auth") }.UseDirect(Msal());
        Assert.Contains("UseMicrosoft(...)", Assert.Throws<InvalidOperationException>(() => withServer.Validate(returnsToScheme: false)).Message);
    }

    [Fact]
    public void Two_ways_without_a_server_are_refused()
    {
        var options = new SpineAuthenticationOptions().UseDirect(Msal());
        Assert.Throws<InvalidOperationException>(() => options.UseIdentityProvider(p => { p.Authority = new Uri("https://id.example.com/"); p.ClientId = "app"; }));
    }

    [Fact]
    public void UseIdentityProvider_goes_through_the_same_hook()
    {
        var options = new SpineAuthenticationOptions { CallbackScheme = "se.example.app" }
            .UseIdentityProvider(p => { p.Authority = new Uri("https://id.example.com/"); p.ClientId = "app"; p.Name = "auth0"; });

        Assert.Equal("auth0", options.Direct!.Name);
        Assert.True(options.Direct.UsesBrowser);
        options.Validate(returnsToScheme: true);
    }
}
