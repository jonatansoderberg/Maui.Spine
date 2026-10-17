namespace Plugin.Maui.Spine.Authentication;

/// <summary>Adds Sign in with Google to Spine sign-in.</summary>
public static class SpineGoogleExtensions
{
    /// <summary>
    /// Sign in with Google: Credential Manager's sheet on Android, the server's broker in the browser elsewhere. The
    /// server configures <c>Google(o => { o.ClientIds.Add(…); o.WebClientId = …; o.WebClientSecret = …; })</c>.
    /// </summary>
    /// <param name="options">The sign-in options.</param>
    /// <param name="configure">Sets the web client id Android's tokens are issued for.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <example>
    /// <code>
    /// o.AddAuthentication(a =>
    /// {
    ///     a.Server = new Uri("https://api.example.com/auth");
    ///     a.CallbackScheme = "se.example.app";
    ///     a.AddApple();
    ///     a.AddGoogle(g => g.ServerClientId = "….apps.googleusercontent.com");
    /// });
    /// </code>
    /// </example>
    public static SpineAuthenticationOptions AddGoogle(this SpineAuthenticationOptions options, Action<SpineGoogleOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var google = new SpineGoogleOptions();
        configure?.Invoke(google);

        if (GoogleSignIn.IsSupported && string.IsNullOrWhiteSpace(google.ServerClientId))
            throw new InvalidOperationException(
                "Spine sign-in: a.AddGoogle(g => g.ServerClientId = …) needs the OAuth Web client's id from the Google Cloud console on Android, the one the server lists in Google(o => o.ClientIds).");

        return options.AddNative(new NativeProvider("google", GoogleSignIn.IsSupported, _ => new GoogleSignIn(google), FallsBackToBrowser: OperatingSystem.IsAndroid()), google.DisplayName);
    }
}
