namespace Plugin.Maui.Spine.Biometrics;

/// <summary>
/// The app's unlock: one prompt that counts for every guarded page and command for
/// <see cref="SpineBiometricsOptions.UnlockValidFor"/>, and is forgotten when the app goes to the background.
/// </summary>
/// <example>
/// <code>
/// if (await _unlock.RequireAsync("Show the PIN"))
///     Pin = await _cards.GetPinAsync();
/// </code>
/// </example>
public interface IUnlock
{
    /// <summary>Whether a recent unlock still counts.</summary>
    bool IsUnlocked { get; }

    /// <summary>
    /// Completes with <see langword="true"/> at once while a recent unlock counts, otherwise after the user
    /// unlocked. <paramref name="fresh"/> asks even then, for something destructive. Calls made while a
    /// prompt shows share it.
    /// </summary>
    Task<bool> RequireAsync(string reason, bool fresh = false, CancellationToken cancellationToken = default);

    /// <summary>Forgets the unlock, for example on sign-out; the next guard asks again.</summary>
    void Lock();

    /// <summary>Raised when <see cref="IsUnlocked"/> changes through an unlock or <see cref="Lock"/>.</summary>
    event EventHandler? LockChanged;
}
