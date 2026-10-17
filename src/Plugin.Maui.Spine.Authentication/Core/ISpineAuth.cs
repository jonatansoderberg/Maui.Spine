namespace Plugin.Maui.Spine.Authentication;

/// <summary>Sign-in against the app's own server: who is signed in, the sheet, the tokens and the account.</summary>
public interface ISpineAuth
{
    /// <summary>
    /// The signed-in user, or <see langword="null"/>. Read from secure storage when the app starts, before any network
    /// call; <see cref="UserChanged"/> is raised once it is known.
    /// </summary>
    SpineUser? User { get; }

    /// <summary>Whether someone is signed in.</summary>
    bool IsSignedIn => User is not null;

    /// <summary>The ways the app offers, and whether this device has a native sheet for each.</summary>
    IReadOnlyList<SignInProvider> Providers { get; }

    /// <summary>
    /// Signs in: Apple's own sheet when Apple is the only way and the platform has it, otherwise the package's sign-in
    /// sheet (or the app's own, from <c>SignInPage&lt;TPage&gt;()</c>). Answers at once when already signed in.
    /// </summary>
    Task<SignInResult> SignInAsync(CancellationToken cancellationToken = default);

    /// <summary>Signs in with one provider, such as <c>apple</c> or <c>passkey</c>; <c>password</c> opens the sheet.</summary>
    Task<SignInResult> SignInAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>Email and password, for an app's own sign-in page. <see cref="SignInStatus.CodeSent"/> when the email is not verified yet.</summary>
    Task<SignInResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Makes an account. <see cref="SignInStatus.CodeSent"/> when the server sends a code to verify the email.</summary>
    Task<SignInResult> SignUpAsync(string email, string password, string? displayName = null, CancellationToken cancellationToken = default);

    /// <summary>Enters the code from a sign-up or sign-in email; signs in.</summary>
    Task<SignInResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default);

    /// <summary>Asks for a new code for an unverified email.</summary>
    Task<SignInResult> ResendCodeAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Asks for a password-reset code. Answers <see cref="SignInStatus.CodeSent"/> whether or not the email has an account.</summary>
    Task<SignInResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password with the code from the reset email; signs in and signs out every other device.</summary>
    Task<SignInResult> ResetPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// An access token for the app's server with at least a minute left, refreshed when needed; <see langword="null"/>
    /// when signed out. Prefer <c>AddSpineAuthentication()</c> on an <c>HttpClient</c>, which also retries after a 401.
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>A handler that adds the access token, for an <see cref="HttpClient"/> built without <c>IHttpClientFactory</c>.</summary>
    /// <param name="inner">The handler it sends through; a new <see cref="HttpClientHandler"/> when <see langword="null"/>.</param>
    DelegatingHandler CreateHandler(HttpMessageHandler? inner = null);

    /// <summary>Signs out on this device and revokes the refresh token on the server.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the account on the server (which revokes Apple's tokens and every session) and signs out: answers
    /// <see cref="SignInStatus.Deleted"/>, or <see cref="SignInStatus.Failed"/> with why. The App Store asks an app with
    /// sign-up to offer this.
    /// </summary>
    Task<SignInResult> DeleteAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The signed-in user's picture from the provider, kept on the device for a day and forgotten at sign-out: Google's
    /// (through <see cref="SpineUser.PictureUrl"/>), Microsoft's (through Graph). Null when nobody is signed in, the
    /// provider has none (Apple, email and password), or it could not be fetched and none was kept.
    /// </summary>
    /// <example>
    /// <code>
    /// if (await auth.GetPictureAsync() is { } picture)
    ///     Avatar.Source = ImageSource.FromStream(() => new MemoryStream(picture));
    /// </code>
    /// </example>
    /// <returns>The image's bytes, JPEG or PNG as the provider has it.</returns>
    Task<byte[]?> GetPictureAsync(CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);

    /// <summary>
    /// Adds a provider to the signed-in user, through its sheet or the browser like a sign-in; <c>passkey</c> makes a
    /// new passkey for the account. Answers
    /// <see cref="SignInStatus.SignedIn"/> with the user, whose <see cref="SpineUser.Logins"/> now has it, or
    /// <see cref="SignInStatus.Failed"/> with <see cref="SpineAuthErrors.IdentityInUse"/> when another account signs in
    /// with that provider account. Needs a Spine server.
    /// </summary>
    /// <param name="provider">The provider's name, such as <c>google</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<SignInResult> LinkAsync(string provider, CancellationToken cancellationToken = default) => NotHere();

    /// <summary>
    /// Removes a provider, or <c>password</c>, from the signed-in user. The server never removes the last way to sign in:
    /// <see cref="SignInStatus.Failed"/> with <see cref="SpineAuthErrors.LastLogin"/>.
    /// </summary>
    /// <param name="provider">The provider's name, as in <see cref="SpineLoginInfo.Provider"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<SignInResult> UnlinkAsync(string provider, CancellationToken cancellationToken = default) => NotHere();

    /// <summary>Makes a new account for a provider account the server asked about (<see cref="SignInStatus.ChooseAccount"/>), and signs in.</summary>
    /// <param name="pending">The result's <see cref="SignInResult.Pending"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<SignInResult> CreateAccountAsync(SpinePendingIdentity pending, CancellationToken cancellationToken = default) => NotHere();

    /// <summary>
    /// Adds a provider account the server asked about (<see cref="SignInStatus.ChooseAccount"/>) to the signed-in user:
    /// sign in the way the user had before, then call this.
    /// </summary>
    /// <param name="pending">The result's <see cref="SignInResult.Pending"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<SignInResult> LinkPendingAsync(SpinePendingIdentity pending, CancellationToken cancellationToken = default) => NotHere();

    /// <summary>
    /// Makes an account that signs in with a passkey, through the platform's passkey sheet, and signs in.
    /// <paramref name="email"/> is shown but kept unverified, not as the account's email. Needs
    /// <c>Plugin.Maui.Spine.Authentication.Passkeys</c> (<c>a.AddPasskeys()</c>) and the server's <c>Passkeys(...)</c>.
    /// </summary>
    /// <param name="displayName">The name the passkey and the account carry.</param>
    /// <param name="email">An email to show with the passkey; one of the two is needed.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<SignInResult> SignUpWithPasskeyAsync(string? displayName, string? email, CancellationToken cancellationToken = default) =>
        Task.FromResult(SignInResult.Failure("This ISpineAuth has no passkeys."));

    /// <summary>
    /// Offers the device's passkeys in the keyboard's suggestions above the page's username or email field, and signs in
    /// when the user picks one. Start it when the field shows and cancel it when the page goes; a sign-in with a button
    /// stops it by itself. Answers <see cref="SignInStatus.Canceled"/> when stopped, and at once where the device has no
    /// such suggestions. The package's sign-in sheet does this on its own.
    /// </summary>
    /// <param name="cancellationToken">Stops offering them.</param>
    Task<SignInResult> SignInWithPasskeyAutoFillAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SignInResult.Canceled);

    private static Task<SignInResult> NotHere() => Task.FromResult(SignInResult.Failure("This ISpineAuth does not link accounts."));

    /// <summary>Raised after a sign-in, with the user.</summary>
    event EventHandler<SpineUser>? SignedIn;

    /// <summary>Raised after a sign-out, a deletion, or a refresh the server refused.</summary>
    event EventHandler? SignedOut;

    /// <summary>
    /// Raised whenever <see cref="User"/> changes: a sign-in, a sign-out, and at startup when the stored session has been
    /// read. Bind a "signed in as" line to this rather than reading <see cref="User"/> once.
    /// </summary>
    event EventHandler? UserChanged;
}
