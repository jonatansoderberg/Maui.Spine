namespace Plugin.Maui.Spine.Authentication;

/// <summary>How a sign-in ended.</summary>
public enum SignInStatus
{
    /// <summary>The user is signed in.</summary>
    SignedIn,

    /// <summary>The user closed the sheet or the platform's prompt.</summary>
    Canceled,

    /// <summary>The server or the platform refused; <see cref="SignInResult.Error"/> says why.</summary>
    Failed,

    /// <summary>
    /// The server sent an email with a code (sign-up, or a sign-in before the email was verified); the user enters it
    /// with <see cref="ISpineAuth.VerifyEmailAsync"/>.
    /// </summary>
    CodeSent,

    /// <summary>The account was deleted and the user is signed out (<see cref="ISpineAuth.DeleteAccountAsync"/>).</summary>
    Deleted,

    /// <summary>
    /// The provider's account is new to the server, which asks before making an account
    /// (<c>AskBeforeNewAccount</c>). <see cref="SignInResult.Pending"/> holds it: the user makes a new account with
    /// <see cref="ISpineAuth.CreateAccountAsync"/>, or signs in another way and adds it with
    /// <see cref="ISpineAuth.LinkPendingAsync"/>. The package's sheet asks by itself.
    /// </summary>
    ChooseAccount,
}

/// <summary>The outcome of a sign-in step.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="User">The signed-in user, when <see cref="Status"/> is <see cref="SignInStatus.SignedIn"/>.</param>
/// <param name="Error">
/// One of <see cref="SpineAuthErrors"/> when the server refused, or a message naming the path and the HTTP status when
/// the call itself failed, so a misconfigured server is visible rather than looking like a cancel. On a
/// <see cref="SignInStatus.SignedIn"/> from the sign-in sheet's "I've signed in before", why the new provider could not
/// be added to the account.
/// </param>
/// <param name="Pending">The provider's account the server holds, when <see cref="Status"/> is <see cref="SignInStatus.ChooseAccount"/>.</param>
public sealed record SignInResult(SignInStatus Status, SpineUser? User = null, string? Error = null, SpinePendingIdentity? Pending = null)
{
    /// <summary>Signed in as <paramref name="user"/>.</summary>
    public static SignInResult Success(SpineUser user) => new(SignInStatus.SignedIn, user);

    /// <summary>Closed by the user.</summary>
    public static SignInResult Canceled { get; } = new(SignInStatus.Canceled);

    /// <summary>Refused, with why.</summary>
    public static SignInResult Failure(string error) => new(SignInStatus.Failed, Error: error);

    /// <summary>An email with a code is on its way.</summary>
    public static SignInResult Code { get; } = new(SignInStatus.CodeSent);

    /// <summary>A new provider account the server asks about.</summary>
    public static SignInResult Choose(SpinePendingIdentity pending) => new(SignInStatus.ChooseAccount, Pending: pending);
}

/// <summary>A way to sign in that the app offers.</summary>
/// <param name="Name">The provider's name on the server: <c>apple</c>, <c>password</c>, …</param>
/// <param name="IsNative">Signs in with the platform's own sheet on this device.</param>
public sealed record SignInProvider(string Name, bool IsNative);
