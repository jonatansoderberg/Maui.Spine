using Microsoft.Extensions.DependencyInjection.Extensions;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary>Registers Spine biometrics with the MAUI application builder.</summary>
public static class SpineBiometricsExtensions
{
    /// <summary>
    /// Adds <see cref="IBiometricAuth"/>, <see cref="IUnlock"/>, the guard behind <see cref="RequiresUnlockAttribute"/>,
    /// <see cref="PrivacyShieldAttribute"/> and the app lock (<see cref="SpineBiometricsOptions.AppLock"/>).
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
            services.AddSingleton(static sp => new UnlockService(
                sp.GetRequiredService<IBiometricAuth>(),
                sp.GetRequiredService<SpineBiometricsOptions>(),
                new UnlockMessages(),
                sp.GetRequiredService<TimeProvider>()));
            services.AddSingleton<IUnlock>(static sp => sp.GetRequiredService<UnlockService>());
            services.AddSingleton<AppLockService>();
            services.AddSingleton<IMauiInitializeService>(static sp => sp.GetRequiredService<AppLockService>());
            services.AddSpineGuard<RequiresUnlockAttribute, UnlockGuard>();
        }

        configure?.Invoke(options);
        return builder;
    }
}
