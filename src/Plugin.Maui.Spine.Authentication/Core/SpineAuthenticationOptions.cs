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

    /// <summary>
    /// The app's URL scheme a browser sign-in returns to on iOS, Mac Catalyst and Android (<c>{scheme}://callback</c>),
    /// such as <c>se.example.app</c>. The same value goes in the project as <c>SpineAuthenticationCallbackScheme</c>,
    /// which registers it on Android, and on the server in <c>RedirectSchemes</c>. Windows returns to a loopback address.
    /// </summary>
    public string? CallbackScheme { get; set; }

    /// <summary>
    /// The iOS browser sign-in shares no cookies with Safari, so it asks for nothing and remembers nobody (Q10). On by
    /// default; off, iOS first asks "… wants to use … to sign in" and the provider may already know the user.
    /// </summary>
    public bool EphemeralBrowserSession { get; set; } = true;

    internal bool Apple { get; private set; }

    internal List<SignInProvider> BrowserProviders { get; } = [];

    internal Dictionary<string, string> DisplayNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal SpineIdentityProviderOptions? IdentityProvider { get; private set; }

    /// <summary>
    /// A provider the server brokers through the browser: one it configures with <c>WebClientId</c> (Google, an
    /// <c>OpenIdConnect(name, …)</c>), shown as a button in the sign-in sheet.
    /// </summary>
    /// <param name="name">The provider's name on the server, as in <c>/auth/{name}/start</c>.</param>
    /// <param name="displayName">The button's text: <c>Continue with {displayName}</c>.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthenticationOptions AddOpenIdConnect(string name, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        BrowserProviders.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        BrowserProviders.Add(new SignInProvider(name.ToLowerInvariant(), false));
        DisplayNames[name] = displayName;
        return this;
    }

    /// <summary>
    /// Signs in directly at an OpenID Connect provider, without a Spine server (Q7): Auth0, Entra, Keycloak, … The app
    /// is a public client with PKCE, and its access token is the provider's. Use instead of <see cref="Server"/>.
    /// </summary>
    /// <param name="configure">Sets the authority and the client id.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthenticationOptions UseIdentityProvider(Action<SpineIdentityProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        IdentityProvider = new SpineIdentityProviderOptions();
        configure(IdentityProvider);
        return this;
    }

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

    /// <param name="returnsToScheme">Whether a configured way to sign in goes through the browser and back to <see cref="CallbackScheme"/> on this platform.</param>
    internal void Validate(bool returnsToScheme)
    {
        if (returnsToScheme && string.IsNullOrWhiteSpace(CallbackScheme))
            throw new InvalidOperationException(
                "Spine sign-in: a browser sign-in needs a.CallbackScheme, the app's URL scheme, and the same value in the project as <SpineAuthenticationCallbackScheme> for Android.");

        // Uri lowercases a scheme, while Android matches the intent filter's scheme as written, case-sensitively.
        if (CallbackScheme is { } scheme && (scheme != scheme.ToLowerInvariant() || Uri.CheckSchemeName(scheme) is false))
            throw new InvalidOperationException($"Spine sign-in: CallbackScheme \"{scheme}\" must be a lowercase URL scheme, such as se.example.app.");

        if (IdentityProvider is { } provider)
        {
            if (Server is not null || Apple || EmailPassword is not null || BrowserProviders.Count > 0)
                throw new InvalidOperationException(
                    "Spine sign-in: UseIdentityProvider(...) signs in without a Spine server; Server, AddApple, AddEmailPassword and AddOpenIdConnect need one.");

            if (provider.Authority is not { IsAbsoluteUri: true } || string.IsNullOrWhiteSpace(provider.ClientId))
                throw new InvalidOperationException("Spine sign-in: UseIdentityProvider(...) needs an Authority and a ClientId.");

            // The ID token's signature is not checked in the app, which rests on TLS to the provider.
            if (!SpineIdentityProviderOptions.IsSecure(provider.Authority))
                throw new InvalidOperationException($"Spine sign-in: the Authority {provider.Authority} must be https (http only on localhost).");
        }
        else
        {
            _ = ServerRoot;
            if (!Apple && EmailPassword is null && BrowserProviders.Count == 0)
                throw new InvalidOperationException("Spine sign-in: offer at least one way to sign in: a.AddApple(), a.AddEmailPassword() or a.AddOpenIdConnect(...).");
        }
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
