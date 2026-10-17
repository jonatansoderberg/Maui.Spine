namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// The platform's passkey sheet. WebAuthn's options go in as the server's JSON, and the credential comes out as JSON in
/// the shape of <c>PublicKeyCredential.toJSON()</c>, which the server checks. <c>Plugin.Maui.Spine.Authentication.Passkeys</c>
/// adds it with <see cref="SpineAuthenticationOptions.AddPasskeyCeremony"/>.
/// </summary>
internal interface IPasskeyCeremony
{
    /// <summary>Makes a passkey: <c>navigator.credentials.create()</c>.</summary>
    /// <param name="options">WebAuthn's <c>PublicKeyCredentialCreationOptions</c>, as the server sent them.</param>
    /// <param name="cancellationToken">Gives up on the sheet.</param>
    /// <returns>The new credential, or <see langword="null"/> when the user closed the sheet.</returns>
    /// <exception cref="InvalidOperationException">The platform refused; the message is shown as the sign-in's error.</exception>
    Task<string?> CreateAsync(string options, CancellationToken cancellationToken);

    /// <summary>Signs with a passkey: <c>navigator.credentials.get()</c>.</summary>
    /// <param name="options">WebAuthn's <c>PublicKeyCredentialRequestOptions</c>, as the server sent them.</param>
    /// <param name="autoFill">
    /// Offers the passkeys in the keyboard's suggestions above a username field instead of a sheet, until the user
    /// picks one or <paramref name="cancellationToken"/> stops it.
    /// </param>
    /// <param name="cancellationToken">Gives up on the sheet or the suggestions.</param>
    /// <returns>The signed assertion, or <see langword="null"/> when the user closed the sheet.</returns>
    /// <exception cref="InvalidOperationException">The platform refused; the message is shown as the sign-in's error.</exception>
    Task<string?> GetAsync(string options, bool autoFill, CancellationToken cancellationToken);
}

/// <summary>Passkeys, where <paramref name="IsSupported"/>.</summary>
/// <param name="IsSupported">Whether this device has the platform's passkey sheet.</param>
/// <param name="SupportsAutoFill">Whether it also offers passkeys in the keyboard's suggestions.</param>
/// <param name="AllowSignUp">The sign-in sheet offers a new account made with a passkey.</param>
/// <param name="Create">Makes the ceremony, once, from the app's services.</param>
internal sealed record PasskeyProvider(bool IsSupported, bool SupportsAutoFill, bool AllowSignUp, Func<IServiceProvider, IPasskeyCeremony> Create);
