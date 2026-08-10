namespace Orientera.Services.Notifications;

/// <summary>
/// Delivery of a plan to the platform. The plan itself is decided in
/// <see cref="NotificationPlanner"/>, which is why this interface has no opinions — it takes
/// the whole plan and makes the device match it.
/// </summary>
public interface INotificationScheduler
{
    /// <summary>False when the platform cannot schedule, so the UI can say so rather than lie.</summary>
    bool IsSupported { get; }

    Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the device's pending notifications equal <paramref name="plan"/> — anything not in
    /// it is cancelled. Re-planning is therefore idempotent, and a competition that moved or was
    /// unfollowed stops notifying rather than firing from a stale schedule.
    /// </summary>
    Task SyncAsync(IReadOnlyList<PlannedNotification> plan, CancellationToken cancellationToken = default);

    Task CancelAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Where the platform has no scheduling to offer. Says so rather than pretending.</summary>
public sealed class UnsupportedNotificationScheduler : INotificationScheduler
{
    public bool IsSupported => false;

    public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task SyncAsync(IReadOnlyList<PlannedNotification> plan, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CancelAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
