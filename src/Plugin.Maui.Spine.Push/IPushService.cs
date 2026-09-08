namespace Plugin.Maui.Spine.Push;

/// <summary>Whether the app may notify.</summary>
public enum PushStatus
{
    /// <summary>The user has not been asked.</summary>
    NotDetermined,

    /// <summary>The user said no, or turned notifications off later.</summary>
    Denied,

    /// <summary>The user said yes.</summary>
    Authorized,

    /// <summary>Granted quietly: notifications arrive in the Notification Center without interrupting.</summary>
    Provisional,

    /// <summary>This platform has no push, or the app was not built for it.</summary>
    Unsupported,
}

/// <summary>What came of asking the backend to take this installation.</summary>
public enum PushRegistrationResult
{
    /// <summary>The backend accepted the registration.</summary>
    Sent,

    /// <summary>Nothing had changed since last time and the confirm window had not run out.</summary>
    Unchanged,

    /// <summary>No backend is configured, so there is nothing to register with.</summary>
    NoBackend,

    /// <summary>The platform has not issued a token yet, so there is nothing to register.</summary>
    NoToken,

    /// <summary>The backend refused the registration, or could not be reached.</summary>
    Failed,
}

/// <summary>The app's view of its own push registration.</summary>
public interface IPushService
{
    /// <summary>Whether the app may notify, as of the last time the platform was asked.</summary>
    PushStatus Status { get; }

    /// <summary>
    /// The APNs device token or FCM registration token this installation is reached through, and the
    /// one thing a registration cannot go out without. <see langword="null"/> until the platform has
    /// issued one — which on Apple is some time after permission is granted, and on Android means
    /// Firebase has not accepted the app's <c>google-services.json</c>.
    /// </summary>
    string? Token { get; }

    /// <summary>
    /// This installation's id, stable across launches and token changes. Generated on first use and
    /// kept in secure storage.
    /// </summary>
    Task<string> GetInstallationIdAsync();

    /// <summary>The tags this installation is registered with, as the app last set them.</summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// Whether the backend has this installation: permission granted, a token in hand, and a
    /// registration it accepted. An app that also notifies locally can use this to decide which
    /// half sends what, without notifying twice or not at all.
    /// </summary>
    bool IsRegistered { get; }

    /// <summary>Asks the user for permission, if they have not been asked.</summary>
    /// <param name="cancellationToken">Cancels the wait for an answer.</param>
    /// <returns>The status afterwards.</returns>
    Task<PushStatus> RequestPermissionAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the tags and registers if anything changed.</summary>
    /// <param name="tags">The complete set of tags the app wants.</param>
    /// <param name="cancellationToken">Cancels the registration.</param>
    /// <returns>What came of registering them. Tags are kept locally either way.</returns>
    Task<PushRegistrationResult> SetTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

    /// <summary>Adds tags to the ones already set.</summary>
    /// <param name="tags">The tags to add.</param>
    /// <param name="cancellationToken">Cancels the registration.</param>
    /// <returns>What came of registering them. Tags are kept locally either way.</returns>
    Task<PushRegistrationResult> AddTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

    /// <summary>Removes tags from the ones already set.</summary>
    /// <param name="tags">The tags to remove.</param>
    /// <param name="cancellationToken">Cancels the registration.</param>
    /// <returns>What came of registering them. Tags are kept locally either way.</returns>
    Task<PushRegistrationResult> RemoveTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the registration to the backend if anything changed since last time — token, tags,
    /// versions, Live Activity tokens — and once every <see cref="SpinePushOptions.Confirm"/>
    /// regardless, so the server can see it is alive. Called at launch and on every foreground.
    /// </summary>
    /// <param name="force">
    /// Sends even when nothing changed. For the case the fingerprint cannot see: a register that
    /// lost the row. Nothing on the wire tells a device it is no longer registered, so an app that
    /// offers the user a "register again" needs a way past the skip.
    /// </param>
    /// <param name="cancellationToken">Cancels the registration.</param>
    /// <returns>What came of it, so a caller can tell "nothing to do" from "could not".</returns>
    Task<PushRegistrationResult> RefreshAsync(bool force = false, CancellationToken cancellationToken = default);

    /// <summary>Removes the registration from the backend. The app stops receiving push.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task UnregisterAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the system settings page where the user can turn notifications back on.</summary>
    Task OpenSettingsAsync();
}
