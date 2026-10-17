using Microsoft.Identity.Client;
using Microsoft.Maui.LifecycleEvents;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>Adds Sign in with Microsoft to Spine sign-in.</summary>
public static class SpineMicrosoftExtensions
{
    /// <summary>
    /// Sign in with Microsoft through MSAL, with its ID token sent to the Spine server like Apple's and Google's. The
    /// server configures <c>Microsoft(o => { o.ClientId = …; o.Tenant = …; })</c>.
    /// </summary>
    /// <param name="options">The sign-in options.</param>
    /// <param name="configure">Sets the client id and the tenant.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <example>
    /// <code>
    /// o.AddAuthentication(a =>
    /// {
    ///     a.Server = new Uri("https://api.example.com/auth");
    ///     a.AddApple();
    ///     a.AddMicrosoft(m => m.ClientId = "…");
    /// });
    /// </code>
    /// </example>
    public static SpineAuthenticationOptions AddMicrosoft(this SpineAuthenticationOptions options, Action<SpineMicrosoftOptions> configure)
    {
        var microsoft = Configure(options, configure);
        return options.AddNative(
            new NativeProvider("microsoft", IsSupported: true, sp => new MicrosoftNativeSignIn(new MicrosoftClient(microsoft, options, sp.GetRequiredService<IBrowserSignIn>())),
                UsesBrowser: OperatingSystem.IsMacCatalyst()),
            microsoft.DisplayName);
    }

    /// <summary>
    /// Signs in with Microsoft without a Spine server, in place of <c>Server</c>: <c>GetAccessTokenAsync</c> and the
    /// <c>HttpClient</c> handler hand out MSAL's access token for <see cref="SpineMicrosoftOptions.Scopes"/>, for an
    /// API that Entra protects. MSAL keeps the tokens and refreshes them.
    /// </summary>
    /// <param name="options">The sign-in options.</param>
    /// <param name="configure">Sets the client id, the tenant and the API's scopes.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <example>
    /// <code>
    /// o.AddAuthentication(a => a.UseMicrosoft(m =>
    /// {
    ///     m.ClientId = "…";
    ///     m.Tenant = "contoso.onmicrosoft.com";
    ///     m.Scopes.Add("api://orders/access_as_user");
    /// }));
    /// </code>
    /// </example>
    public static SpineAuthenticationOptions UseMicrosoft(this SpineAuthenticationOptions options, Action<SpineMicrosoftOptions> configure)
    {
        var microsoft = Configure(options, configure);
        if (microsoft.Scopes.Count == 0)
            throw new InvalidOperationException("Spine sign-in: UseMicrosoft(m => m.Scopes.Add(…)) needs the scopes of the API the access token is for.");

        return options.UseDirect(new DirectProvider(microsoft.Name, "UseMicrosoft(...)", UsesBrowser: OperatingSystem.IsMacCatalyst(),
            sp => new MicrosoftDirectSignIn(new MicrosoftClient(microsoft, options, sp.GetRequiredService<IBrowserSignIn>()), microsoft)));
    }

    /// <summary>
    /// Passes MSAL the answers that come back to the app: from the broker on iOS (<c>OpenUrl</c>) and from the
    /// browser or the broker on Android (<c>OnActivityResult</c>). <c>UseSpine()</c> calls it for an app that references
    /// this package; calling it again is harmless.
    /// </summary>
    /// <param name="builder">The app's builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static MauiAppBuilder UseSpineAuthenticationMicrosoft(this MauiAppBuilder builder)
    {
        if (builder.Services.Any(static d => d.ServiceType == typeof(LifecycleHooked))) return builder;
        builder.Services.AddSingleton<LifecycleHooked>();

        builder.ConfigureLifecycleEvents(events =>
        {
#if IOS
            static bool Continue(Foundation.NSUrl url)
            {
                if (url.Scheme?.StartsWith("msauth", StringComparison.OrdinalIgnoreCase) != true) return false;
                AuthenticationContinuationHelper.SetBrokerContinuationEventArgs(url);
                return true;
            }

            events.AddiOS(ios => ios
                .OpenUrl((_, url, _) => Continue(url))
                .SceneOpenUrl((_, contexts) =>
                {
                    var handled = false;
                    foreach (var context in contexts) handled |= Continue(context.Url);
                    return handled;
                }));
#elif ANDROID
            events.AddAndroid(android => android.OnActivityResult((_, requestCode, resultCode, data) =>
                AuthenticationContinuationHelper.SetAuthenticationContinuationEventArgs(requestCode, resultCode, data!)));
#endif
        });
        return builder;
    }

    private static SpineMicrosoftOptions Configure(SpineAuthenticationOptions options, Action<SpineMicrosoftOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);
        var microsoft = new SpineMicrosoftOptions();
        configure(microsoft);

        if (string.IsNullOrWhiteSpace(microsoft.ClientId))
            throw new InvalidOperationException("Spine sign-in: Sign in with Microsoft needs m.ClientId, the app registration's Application (client) id in Entra.");

        return microsoft;
    }

    private sealed class LifecycleHooked;
}
