namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// A provider's own sign-in sheet on the platforms that have one, such as Credential Manager for Google on Android. A
/// provider package adds it with <see cref="SpineAuthenticationOptions.AddNative"/>; elsewhere the provider goes
/// through the server's broker in the browser.
/// </summary>
internal interface INativeSignIn
{
    /// <summary>Shows the provider's sheet.</summary>
    /// <param name="nonce">The server's nonce (<c>GET /auth/nonce</c>), which the ID token must carry.</param>
    /// <param name="cancellationToken">Gives up on the sheet.</param>
    /// <returns>The provider's ID token, or null when the user closed the sheet.</returns>
    /// <exception cref="InvalidOperationException">The sheet failed; the message is shown as the sign-in's error.</exception>
    /// <exception cref="PlatformNotSupportedException">This device cannot show the sheet after all; the server's broker takes over.</exception>
    Task<NativeCredential?> SignInAsync(string nonce, CancellationToken cancellationToken);

    /// <summary>Called after every sign-out, so the provider does not pick the same account again by itself.</summary>
    Task SignedOutAsync();
}

/// <summary>What a provider's sheet returns: the ID token the server checks at <c>POST /auth/{provider}/token</c>.</summary>
internal sealed record NativeCredential(string IdToken, string? DisplayName = null);

/// <summary>A provider with a sheet of its own where <paramref name="IsSupported"/>, and the broker elsewhere.</summary>
/// <param name="Name">The provider's name on the server, such as <c>google</c>.</param>
/// <param name="IsSupported">Whether this platform has the sheet.</param>
/// <param name="Create">Makes the sheet's implementation, once, from the app's services.</param>
internal sealed record NativeProvider(string Name, bool IsSupported, Func<IServiceProvider, INativeSignIn> Create);
