using Microsoft.Extensions.DependencyInjection.Extensions;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary>Registers Spine biometrics with the MAUI application builder.</summary>
public static class SpineBiometricsExtensions
{
    /// <summary>
    /// Adds <see cref="IBiometricAuth"/>, <see cref="IUnlock"/> and the guard behind <see cref="RequiresUnlockAttribute"/>.
    /// <c>UseSpine()</c> calls it for an app that references this package; call it yourself only to configure the
    /// options, before or after <c>UseSpine()</c>. The first call registers the services; every call applies its
    /// <paramref name="configure"/> to the same options instance.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseSpineBiometrics(o => o.UnlockValidFor = TimeSpan.FromMinutes(5));
    /// </code>
    /// </example>
    public static MauiAppBuilder UseSpineBiometrics(this MauiAppBuilder builder, Action<SpineBiometricsOptions>? configure = null)
    {
        var services = builder.Services;

        if (services.FirstOrDefault(static d => d.ServiceType == typeof(SpineBiometricsOptions) && !d.IsKeyedService)?.ImplementationInstance
            is not SpineBiometricsOptions options)
        {
            options = new SpineBiometricsOptions();
            BiometricsStrings.EnsureRegistered();

            services.AddSingleton(options);
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IBiometricAuth, BiometricAuth>();
            services.AddSingleton(static sp =>
            {
                var unlock = new UnlockService(
                    sp.GetRequiredService<IBiometricAuth>(),
                    sp.GetRequiredService<SpineBiometricsOptions>(),
                    new UnlockMessages(),
                    sp.GetRequiredService<TimeProvider>());

                unlock.Unlocked += static (sender, _) => ForgetWhenStopped((UnlockService)sender!);
                return unlock;
            });
            services.AddSingleton<IUnlock>(static sp => sp.GetRequiredService<UnlockService>());
            services.AddSpineGuard<RequiresUnlockAttribute, UnlockGuard>();
        }

        configure?.Invoke(options);
        return builder;
    }

    private static readonly HashSet<Window> _watched = [];

    /// <summary>
    /// Forgets the unlock when a window stops (the app goes to the background, or the desktop window is
    /// minimised), so coming back asks again.
    /// </summary>
    private static void ForgetWhenStopped(UnlockService unlock)
    {
        if (Application.Current is not { } app)
            return;

        foreach (var window in app.Windows)
        {
            if (_watched.Add(window))
                window.Stopped += (_, _) => unlock.Lock();
        }
    }
}
