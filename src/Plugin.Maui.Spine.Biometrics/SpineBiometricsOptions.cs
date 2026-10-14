namespace Plugin.Maui.Spine.Biometrics;

/// <summary>Options for <c>UseSpineBiometrics</c>.</summary>
public sealed class SpineBiometricsOptions
{
    /// <summary>
    /// How long an unlock counts for later guards and <see cref="IUnlock.RequireAsync"/> calls. Defaults to one
    /// minute. A guard with <see cref="RequiresUnlockAttribute.Fresh"/> asks every time.
    /// </summary>
    public TimeSpan UnlockValidFor { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// What a guard does on a device with no passcode and no biometric. <see cref="UnavailablePolicy.Allow"/>
    /// (the default) lets it through: there is no lock to ask for, and a guard is a gate in the UI.
    /// </summary>
    public UnavailablePolicy WhenUnavailable { get; set; } = UnavailablePolicy.Allow;

    /// <summary>
    /// Turns on the app lock: the app starts behind an unlock screen and shows it again when it comes back
    /// after <see cref="AppLockOptions.LockAfter"/> in the background. Unlocking it counts for
    /// <see cref="RequiresUnlockAttribute"/> for <see cref="UnlockValidFor"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseSpineBiometrics(o => o.AppLock(a =>
    /// {
    ///     a.LockAfter = TimeSpan.FromMinutes(5);
    ///     a.IsEnabled = () => Preferences.Get("RequireUnlock", false);
    /// }));
    /// </code>
    /// </example>
    public SpineBiometricsOptions AppLock(Action<AppLockOptions>? configure = null)
    {
        HasAppLock = true;
        configure?.Invoke(Lock);
        return this;
    }

    /// <summary>Whether <see cref="AppLock"/> was called.</summary>
    internal bool HasAppLock { get; private set; }

    /// <summary>The app lock's options; <see cref="AppLockOptions.LockAfter"/> applies to relocking pages without it too.</summary>
    internal AppLockOptions Lock { get; } = new();
}

/// <summary>What to do when the device has nothing to unlock with.</summary>
public enum UnavailablePolicy
{
    /// <summary>Let the page open or the command run.</summary>
    Allow,

    /// <summary>Refuse, with a message that asks the user to set up a screen lock.</summary>
    Deny,
}
