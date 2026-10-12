namespace Plugin.Maui.Spine.Biometrics;

/// <summary>Options for the app lock, set with <see cref="SpineBiometricsOptions.AppLock"/>.</summary>
public sealed class AppLockOptions
{
    /// <summary>
    /// How long the app may be in the background before it locks. Defaults to zero, so it locks on every
    /// return. On the desktop only minimised time counts. Also used for a page with
    /// <see cref="RequiresUnlockAttribute.Relock"/> when the app lock is off.
    /// </summary>
    public TimeSpan LockAfter { get; set; } = TimeSpan.Zero;

    /// <summary>Whether the app starts locked. Defaults to <see langword="true"/>.</summary>
    public bool LockOnLaunch { get; set; } = true;

    /// <summary>Why the app asks, shown in the prompt and on the lock screen: text, or the key of a Spine string.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// The app's own setting, read when the app starts and each time it comes back; <see langword="null"/>
    /// keeps the lock on.
    /// </summary>
    public Func<bool>? IsEnabled { get; set; }
}
