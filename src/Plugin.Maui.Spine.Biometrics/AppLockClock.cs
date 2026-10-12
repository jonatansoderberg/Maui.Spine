namespace Plugin.Maui.Spine.Biometrics;

/// <summary>When coming back locks the app: the time in the background against <see cref="AppLockOptions.LockAfter"/>.</summary>
internal sealed class AppLockClock(SpineBiometricsOptions options, TimeProvider time)
{
    private DateTimeOffset? _stoppedAt;

    /// <summary>Whether the app lock is configured and the app's own setting has it on.</summary>
    public bool IsAppLockOn => options.HasAppLock && (options.Lock.IsEnabled?.Invoke() ?? true);

    /// <summary>Whether the app starts locked.</summary>
    public bool LocksOnLaunch => IsAppLockOn && options.Lock.LockOnLaunch;

    /// <summary>The window stopped: the app went to the background, or the desktop window was minimised.</summary>
    public void Stopped() => _stoppedAt ??= time.GetUtcNow();

    /// <summary>
    /// The user unlocked. An unlock after the stop means the stop was the prompt's own (Android's device
    /// credential screen is an activity of its own), so it does not count.
    /// </summary>
    public void Unlocked() => _stoppedAt = null;

    /// <summary>
    /// The window came back. Whether it locks: the app was stopped for at least <see cref="AppLockOptions.LockAfter"/>
    /// with no unlock since, and the app lock is on or <paramref name="relock"/> (a relocking page is on screen).
    /// </summary>
    public bool Resumed(bool relock)
    {
        if (_stoppedAt is not { } stoppedAt)
            return false;

        _stoppedAt = null;
        return (relock || IsAppLockOn) && time.GetUtcNow() - stoppedAt >= options.Lock.LockAfter;
    }
}
