namespace Plugin.Maui.Spine.Authentication;

/// <summary>Sign in with Google, set in <c>a.AddGoogle(g => …)</c>.</summary>
public sealed class SpineGoogleOptions
{
    /// <summary>
    /// The OAuth Web client's id from the Google Cloud console, which Credential Manager on Android issues the ID token
    /// for. The server lists it in <c>Google(o => o.ClientIds)</c>. Required on Android; the other platforms sign in
    /// through the server's broker, which uses the server's <c>WebClientId</c>.
    /// </summary>
    public string? ServerClientId { get; set; }

    /// <summary>The button's text: <c>Continue with {DisplayName}</c>.</summary>
    public string DisplayName { get; set; } = "Google";
}
