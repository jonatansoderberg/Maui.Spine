using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>Registers Spine sign-in.</summary>
public static class SpineAuthenticationExtensions
{
    private const string HttpClientName = "Plugin.Maui.Spine.Authentication";

    /// <summary>Turns on sign-in, inside <c>UseSpine</c>.</summary>
    /// <param name="spine">Spine's options.</param>
    /// <param name="configure">Sets the server and the ways to sign in.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.UseSpine(o => o.AddAuthentication(a =>
    /// {
    ///     a.Server = new Uri("https://api.example.com/auth");
    ///     a.AddApple();
    ///     a.AddEmailPassword();
    /// }));
    /// </code>
    /// </example>
    public static SpineOptions AddAuthentication(this SpineOptions spine, Action<SpineAuthenticationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(spine);
        return spine.Configure(configure);
    }

    /// <summary>
    /// Registers <see cref="ISpineAuth"/>, the guard behind <see cref="RequiresSignInAttribute"/> and <see cref="SignInSheet"/>.
    /// <c>UseSpine()</c> calls it for an app that references this package and applies <c>o.AddAuthentication(…)</c>;
    /// calling it again is harmless.
    /// </summary>
    /// <param name="builder">The app's builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static MauiAppBuilder UseSpineAuthentication(this MauiAppBuilder builder)
    {
        var services = builder.Services;
        var spine = services.FirstOrDefault(static d => d.ServiceType == typeof(SpineOptions))?.ImplementationInstance as SpineOptions;

        if (services.FirstOrDefault(static d => d.ServiceType == typeof(SpineAuthenticationOptions) && !d.IsKeyedService)?.ImplementationInstance
            is not SpineAuthenticationOptions options)
        {
            options = new SpineAuthenticationOptions();
            AuthStrings.EnsureRegistered();
            services.AddSingleton(options);
            services.AddHttpClient(HttpClientName);

            services.AddSingleton(sp => new SpineAuthClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName), options.ServerRoot));
            services.AddSingleton(sp => new IdentityProviderClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                options.IdentityProvider ?? throw new InvalidOperationException("Spine sign-in: no UseIdentityProvider(...)."),
                sp.GetService<TimeProvider>() ?? TimeProvider.System));
            services.AddSingleton<IBrowserSignIn>(sp => new PlatformBrowserSignIn(options));
            services.AddSingleton(sp => new SpineAuthSession(
                options.IdentityProvider is null ? sp.GetRequiredService<SpineAuthClient>() : sp.GetRequiredService<IdentityProviderClient>(),
                new SecureSpineAuthStorage(options.StorageKey),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Authentication") ?? NullLogger.Instance));
            services.AddSingleton(sp => new SpineAuthFlows(sp.GetRequiredService<SpineAuthClient>(), sp.GetRequiredService<SpineAuthSession>()));
            services.AddSingleton<ISpineAuth, SpineAuth>();
            services.AddSpineGuard<RequiresSignInAttribute, SignInGuard>();

            // Reads the stored session at startup, so User is known before the first page asks.
            services.AddSingleton<IMauiInitializeService>(static sp => new StartupLoad(sp.GetRequiredService<SpineAuthSession>()));

        }

        // The sheet lives in this package: the navigation registry is built on first use, so adding the assembly now is in
        // time. Checked on every call, because an app that calls this before UseSpine has no Spine options yet.
        var assembly = typeof(SignInSheet).Assembly;
        if (spine is not null && !spine.Assemblies.Contains(assembly))
        {
            spine.AddAssembly(assembly);
            services.AddTransient<SignInSheet>();
            services.AddTransient<SignInSheetViewModel>();
        }

        if (spine?.ApplyConfiguration(options) == true)
        {
            // Windows returns to a loopback address and needs no scheme.
            var browser = options.IdentityProvider is not null || options.BrowserProviders.Count > 0 || options.Apple && !AppleSignIn.IsSupported;
            options.Validate(returnsToScheme: browser && !OperatingSystem.IsWindows());
        }

        // PushNotifications resolves it when it is referenced; there only when the app asks for the link (Q11).
        if (options.LinkPushNotifications && !services.Any(static d => d.ServiceType == typeof(ISignedInSession)))
            services.AddSingleton<ISignedInSession>(static sp => sp.GetRequiredService<SpineAuthSession>());

        return builder;
    }

    private sealed class StartupLoad(SpineAuthSession session) : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) => _ = session.LoadAsync();
    }
}
