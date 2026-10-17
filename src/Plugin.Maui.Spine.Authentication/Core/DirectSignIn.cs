namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// Signing in without a Spine server: the provider's own tokens are the session's, and the provider refreshes and
/// revokes them. <c>UseIdentityProvider</c> (any OpenID Connect provider) and <c>UseMicrosoft</c> (MSAL) are two.
/// </summary>
internal interface IDirectSignIn : ITokenEndpoint
{
    /// <summary>Signs in, with whatever UI the provider shows.</summary>
    /// <returns>The tokens, or why there are none.</returns>
    Task<(SpineTokens? Tokens, SignInResult? Problem)> SignInAsync(CancellationToken cancellationToken);

    /// <summary>The signed-in user's picture when the provider has one without an address (Microsoft's Graph).</summary>
    Task<byte[]?> PictureAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
}

/// <summary>The way an app signs in without a Spine server.</summary>
/// <param name="Name">The name <see cref="SpineUser.Provider"/> and <see cref="ISpineAuth.Providers"/> carry.</param>
/// <param name="Method">How the app turned it on, for error messages, such as <c>UseIdentityProvider(...)</c>.</param>
/// <param name="UsesBrowser">Whether it returns from the browser to <see cref="SpineAuthenticationOptions.CallbackScheme"/> on this platform.</param>
/// <param name="Create">Makes it, once, from the app's services.</param>
internal sealed record DirectProvider(string Name, string Method, bool UsesBrowser, Func<IServiceProvider, IDirectSignIn> Create);

/// <summary><see cref="IdentityProviderClient"/> with the browser it signs in through.</summary>
internal sealed class IdentityProviderSignIn(IdentityProviderClient client, IBrowserSignIn browser) : IDirectSignIn
{
    public Task<(SpineTokens? Tokens, SignInResult? Problem)> SignInAsync(CancellationToken cancellationToken) => client.SignInAsync(browser, cancellationToken);

    public Task<ServerAnswer> RefreshAsync(string refreshToken, SpineUser? user, CancellationToken cancellationToken) =>
        client.RefreshAsync(refreshToken, user, cancellationToken);

    public Task<ServerAnswer> RevokeAsync(string refreshToken, CancellationToken cancellationToken) => client.RevokeAsync(refreshToken, cancellationToken);
}
