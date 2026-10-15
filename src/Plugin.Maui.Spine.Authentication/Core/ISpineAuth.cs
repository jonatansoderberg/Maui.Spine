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

    /// <summary>Signs in with one provider, such as <c>apple</c>; <c>password</c> opens the sheet.</summary>
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
