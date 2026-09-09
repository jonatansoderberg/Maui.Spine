namespace Plugin.Maui.Spine.Push.Services;

/// <summary>Where the platform has no scheduling to offer. Says so rather than pretending.</summary>
internal sealed class UnsupportedLocalNotifications : ILocalNotificationService
{
    public bool IsSupported => false;

    public Task SyncAsync(IEnumerable<LocalNotification> plan, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<LocalNotification>> PendingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalNotification>>([]);

    public Task CancelAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
