namespace Plugin.Maui.Spine.Authentication;

/// <summary>How the app signs in: its server and the ways it offers. Set inside <c>UseSpine(o => o.AddAuthentication(a => …))</c>.</summary>
public sealed partial class SpineAuthenticationOptions
{
    /// <summary>
    /// Where the server's sign-in endpoints live: the prefix given to <c>MapSpineAuthentication</c>, such as
    /// <c>https://api.example.com/auth</c>. Required.
    /// </summary>
    public Uri? Server { get; set; }

    /// <summary>
    /// Registers the device for push notifications with the signed-in user's access token, and again on every sign-in
    /// and sign-out, when <c>Plugin.Maui.Spine.PushNotifications</c> is referenced and its own <c>AuthorizationHeader</c>
    /// is unset. The server then tags the device <c>user:&lt;id&gt;</c>. Off by default.
    /// </summary>
    public bool LinkPushNotifications { get; set; }

    /// <summary>The key the refresh token and the user are kept under in secure storage.</summary>
    public string StorageKey { get; set; } = "spine.auth.session";

    internal bool Apple { get; private set; }

    internal SpineEmailPasswordOptions? EmailPassword { get; private set; }

    /// <summary>Sign in with Apple, with the platform's own sheet on iOS and Mac Catalyst.</summary>
    /// <returns>The same options, for chaining.</returns>
    /// <remarks>
    /// The app sets <c>&lt;SpineSignInWithApple&gt;true&lt;/SpineSignInWithApple&gt;</c> in its project, and its App ID
    /// has the Sign in with Apple capability. The server configures <c>Apple(o => o.BundleIds.Add(…))</c>.
    /// </remarks>
    public SpineAuthenticationOptions AddApple()
    {
        Apple = true;
        return this;
    }

    /// <summary>Email and password, in the package's sign-in sheet or through <see cref="ISpineAuth"/>'s methods.</summary>
    /// <param name="configure">Changes what the sheet offers.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthenticationOptions AddEmailPassword(Action<SpineEmailPasswordOptions>? configure = null)
    {
        EmailPassword = new SpineEmailPasswordOptions();
        configure?.Invoke(EmailPassword);
        return this;
    }

    internal Uri ServerRoot
    {
        get
        {
            if (Server is not { IsAbsoluteUri: true } server)
                throw new InvalidOperationException("Spine sign-in: set Server in o.AddAuthentication(a => a.Server = new Uri(\"https://…/auth\")).");

            return server.AbsoluteUri.EndsWith('/') ? server : new Uri(server.AbsoluteUri + "/");
        }
    }

    internal void Validate()
    {
        _ = ServerRoot;
        if (!Apple && EmailPassword is null)
            throw new InvalidOperationException("Spine sign-in: offer at least one way to sign in: a.AddApple() or a.AddEmailPassword().");
    }
}

/// <summary>What the sign-in sheet offers for email and password. The server decides what it allows.</summary>
public sealed class SpineEmailPasswordOptions
{
    /// <summary>The sheet offers "Create account". On by default; turn it off when the server's <c>AllowSignUp</c> is off.</summary>
    public bool AllowSignUp { get; set; } = true;

    /// <summary>The sheet offers "Forgot password?". On by default.</summary>
    public bool AllowReset { get; set; } = true;
}
