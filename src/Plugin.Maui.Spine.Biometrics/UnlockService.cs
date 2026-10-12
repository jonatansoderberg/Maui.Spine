namespace Plugin.Maui.Spine.Biometrics;

/// <summary>The messages <see cref="UnlockService"/> shows; the app's alerts, or a test's record.</summary>
internal interface IUnlockMessages
{
    Task ShowLockedOutAsync();

    Task ShowUnavailableAsync();
}

/// <summary>The <see cref="IUnlock"/> behind the guards: grace period, shared prompts, the unavailable policy.</summary>
internal sealed class UnlockService(IBiometricAuth auth, SpineBiometricsOptions options, IUnlockMessages messages, TimeProvider time) : IUnlock
{
    private readonly Lock _gate = new();
    private DateTimeOffset? _unlockedAt;
    private Task<bool>? _pending;

    public bool IsUnlocked => _unlockedAt is { } at && time.GetUtcNow() - at < options.UnlockValidFor;

    public event EventHandler? LockChanged;

    /// <summary>Raised after a successful unlock, so the app can forget it when the window stops.</summary>
    public event EventHandler? Unlocked;

    public Task<bool> RequireAsync(string reason, bool fresh = false, CancellationToken cancellationToken = default)
    {
        if (!fresh && IsUnlocked)
            return Task.FromResult(true);

        lock (_gate)
        {
            if (_pending is { IsCompleted: false } pending)
                return pending;

            return _pending = AskAsync(reason, cancellationToken);
        }
    }

    public void Lock()
    {
        if (_unlockedAt is null)
            return;

        _unlockedAt = null;
        LockChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> AskAsync(string reason, CancellationToken cancellationToken)
    {
        var availability = await auth.GetAvailabilityAsync();

        if (!availability.CanAuthenticate)
            return await UnavailableAsync();

        switch (await auth.AuthenticateAsync(reason, cancellationToken: cancellationToken))
        {
            case BiometricResult.Success:
                _unlockedAt = time.GetUtcNow();
                LockChanged?.Invoke(this, EventArgs.Empty);
                Unlocked?.Invoke(this, EventArgs.Empty);
                return true;

            case BiometricResult.LockedOut:
                await messages.ShowLockedOutAsync();
                return false;

            case BiometricResult.NotEnrolled or BiometricResult.NotAvailable:
                return await UnavailableAsync();

            default:
                return false;
        }
    }

    private async Task<bool> UnavailableAsync()
    {
        if (options.WhenUnavailable is UnavailablePolicy.Allow)
            return true;

        await messages.ShowUnavailableAsync();
        return false;
    }
}
