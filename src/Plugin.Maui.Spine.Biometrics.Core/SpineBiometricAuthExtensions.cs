namespace Plugin.Maui.Spine.Biometrics;

/// <summary>Registers <c>Plugin.Maui.Spine.Biometrics.Core</c> with a <see cref="MauiAppBuilder"/>.</summary>
public static class SpineBiometricAuthExtensions
{
    /// <summary>
    /// Adds <see cref="IBiometricAuth"/>, the platform's biometric prompt. <c>UseSpine()</c> and
    /// <c>UseSpineBiometrics()</c> call it; call it yourself in an app without Spine's core.
    /// Calling it more than once registers the prompt once.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseMauiApp&lt;App&gt;().UseSpineBiometricAuth();
    /// </code>
    /// </example>
    public static MauiAppBuilder UseSpineBiometricAuth(this MauiAppBuilder builder)
    {
        if (builder.Services.Any(static s => s.ServiceType == typeof(IBiometricAuth)))
            return builder;

        BiometricAuthStrings.EnsureRegistered();
        builder.Services.AddSingleton<IBiometricAuth, BiometricAuth>();
        return builder;
    }
}
