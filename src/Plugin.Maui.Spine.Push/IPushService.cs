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

/// <summary>The app's view of its own push registration.</summary>
public interface IPushService
{
    /// <summary>Whether the app may notify, as of the last time the platform was asked.</summary>
    PushStatus Status { get; }

    /// <summary>
    /// This installation's id, stable across launches and token changes. Generated on first use and
    /// kept in secure storage.
    /// </summary>
    Task<string> GetInstallationIdAsync();

    /// <summary>The tags this installation is registered with, as the app last set them.</summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>Asks the user for permission, if they have not been asked.</summary>
    /// <param name="cancellationToken">Cancels the wait for an answer.</param>
    /// <returns>The status afterwards.</returns>
    Task<PushStatus> RequestPermissionAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the tags and registers if anything changed.</summary>
    /// <param name="tags">The complete set of tags the app wants.</param>
    /// <param name="cancellationToken">Cancels the registration.</param>
    Task SetTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

    /// <summary>Adds tags to the ones already set.</summary>
    /// <param name="tags">The tags to add.</param>
    /// <param name="cancellationToken">Cancels the registration.</param>
    Task AddTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

    /// <summary>Removes tags from the ones already set.</summary>
    /// <param name="tags">The tags to remove.</param>
    /// <param name="cancellationToken">Cancels the registration.</param>
    Task RemoveTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the registration to the backend if anything changed since last time — token, tags,
    /// versions, Live Activity tokens — and once a day regardless, so the server can see it is alive.
    /// Called at launch and on every foreground.
    /// </summary>
    /// <param name="cancellationToken">Cancels the registration.</param>
    /// <returns><see langword="true"/> when something was sent.</returns>
    Task<bool> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the registration from the backend. The app stops receiving push.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task UnregisterAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the system settings page where the user can turn notifications back on.</summary>
    Task OpenSettingsAsync();
}
