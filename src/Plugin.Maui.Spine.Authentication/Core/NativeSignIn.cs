namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// A provider's own sign-in sheet on the platforms that have one, such as Credential Manager for Google on Android. A
/// provider package adds it with <see cref="SpineAuthenticationOptions.AddNative"/>; elsewhere the provider goes
/// through the server's broker in the browser.
/// </summary>
internal interface INativeSignIn
{
    /// <summary>
    /// Whether the provider puts the server's nonce in its ID token. MSAL picks its own nonce, so Microsoft's token
    /// goes to the server without one, which the server allows for Microsoft only.
    /// </summary>
    bool UsesNonce => true;

    /// <summary>Shows the provider's sheet.</summary>
    /// <param name="nonce">The server's nonce (<c>GET /auth/nonce</c>), which the ID token must carry; null when <see cref="UsesNonce"/> is false.</param>
    /// <param name="cancellationToken">Gives up on the sheet.</param>
    /// <returns>The provider's ID token, or null when the user closed the sheet.</returns>
    /// <exception cref="InvalidOperationException">The sheet failed; the message is shown as the sign-in's error.</exception>
    /// <exception cref="PlatformNotSupportedException">This device cannot show the sheet after all; the server's broker takes over.</exception>
    Task<NativeCredential?> SignInAsync(string? nonce, CancellationToken cancellationToken);

    /// <summary>Called after every sign-out, so the provider does not pick the same account again by itself.</summary>
    Task SignedOutAsync();

    /// <summary>
    /// The server refused the credential (another tenant, an audience it does not take). A provider that remembers the
    /// account forgets it, so the next sign-in offers a choice instead of the same refused account.
    /// </summary>
    Task RefusedAsync(NativeCredential credential) => Task.CompletedTask;

    /// <summary>The signed-in user's picture when the provider has one without an address (Microsoft's Graph).</summary>
    Task<byte[]?> PictureAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
}

/// <summary>What a provider's sheet returns: the ID token the server checks at <c>POST /auth/{provider}/token</c>.</summary>
/// <param name="IdToken">The provider's ID token.</param>
/// <param name="DisplayName">The user's name, when the provider gave it outside the token.</param>
/// <param name="AccountId">The provider's own handle for the account, for <see cref="INativeSignIn.RefusedAsync"/>.</param>
internal sealed record NativeCredential(string IdToken, string? DisplayName = null, string? AccountId = null);

/// <summary>A provider with a sheet of its own where <paramref name="IsSupported"/>, and the broker elsewhere.</summary>
/// <param name="Name">The provider's name on the server, such as <c>google</c>.</param>
/// <param name="IsSupported">Whether this platform has the sheet.</param>
/// <param name="Create">Makes the sheet's implementation, once, from the app's services.</param>
/// <param name="UsesBrowser">Whether the sheet itself returns from the browser to <see cref="SpineAuthenticationOptions.CallbackScheme"/> on this platform (MSAL on Mac Catalyst).</param>
/// <param name="FallsBackToBrowser">Whether the sheet can find at run time that the device lacks it (Credential Manager without Google Play services) and hand over to the broker.</param>
internal sealed record NativeProvider(string Name, bool IsSupported, Func<IServiceProvider, INativeSignIn> Create, bool UsesBrowser = false, bool FallsBackToBrowser = false);
