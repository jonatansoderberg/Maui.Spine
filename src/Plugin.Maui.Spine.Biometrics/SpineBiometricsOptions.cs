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
}

/// <summary>What to do when the device has nothing to unlock with.</summary>
public enum UnavailablePolicy
{
    /// <summary>Let the page open or the command run.</summary>
    Allow,

    /// <summary>Refuse, with a message that asks the user to set up a screen lock.</summary>
    Deny,
}
