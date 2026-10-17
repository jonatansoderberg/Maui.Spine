namespace Plugin.Maui.Spine.Authentication;

/// <summary>Sign in with Microsoft through MSAL, set in <c>a.AddMicrosoft(m => …)</c> or <c>a.UseMicrosoft(m => …)</c>.</summary>
public sealed class SpineMicrosoftOptions
{
    /// <summary>The app registration's Application (client) id in Entra. Required.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Who can sign in: <c>common</c> (work, school and personal accounts), <c>organizations</c>, <c>consumers</c>, or a
    /// tenant id or domain. Matches the server's <c>Microsoft(o => o.Tenant)</c> with <c>AddMicrosoft</c>.
    /// </summary>
    public string Tenant { get; set; } = "common";

    /// <summary>
    /// The scopes asked for. With <c>UseMicrosoft</c>, those of the API the access token is for, such as
    /// <c>api://…/access_as_user</c> or Graph's <c>User.Read</c>; required. With <c>AddMicrosoft</c>, empty is enough:
    /// the server needs only the ID token.
    /// </summary>
    public IList<string> Scopes { get; } = [];

    /// <summary>
    /// Signs in through Microsoft Authenticator or Company Portal on iOS and Android when one is installed, and WAM on
    /// Windows. On by default; the system browser is used where there is no broker.
    /// </summary>
    public bool UseBroker { get; set; } = true;

    /// <summary>
    /// The redirect URI registered in Entra, when it is not the platform's default: <c>msauth.{bundle id}://auth</c>
    /// on iOS, <c>msauth://{package}/{signature hash}</c> on Android, <c>{CallbackScheme}://callback</c> on Mac
    /// Catalyst, and <c>ms-appx-web://microsoft.aad.brokerplugin/{client id}</c> with WAM on Windows.
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// Asks for Microsoft Graph's <c>User.Read</c> along with the sign-in, so <c>ISpineAuth.GetPictureAsync</c> can fetch
    /// the user's picture without a second prompt. On by default; off, the picture is null.
    /// </summary>
    public bool Picture { get; set; } = true;

    /// <summary>The button's text: <c>Continue with {DisplayName}</c>.</summary>
    public string DisplayName { get; set; } = "Microsoft";

    /// <summary>The name <see cref="SpineUser.Provider"/> carries with <c>UseMicrosoft</c>.</summary>
    public string Name { get; set; } = "microsoft";
}
