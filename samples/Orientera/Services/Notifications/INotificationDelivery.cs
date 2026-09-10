namespace Orientera.Services.Notifications;

/// <summary>
/// Hands a plan to the platform. What the plan contains is decided in
/// <see cref="NotificationPlanner"/>, which is why this interface has no opinions — it takes the
/// whole plan and makes the device match it.
/// </summary>
/// <remarks>
/// Spine's <c>ILocalNotificationService</c> does the work; this seam exists because this folder is
/// compiled into <c>Orientera.Tests</c> on plain .NET, where the MAUI package does not exist. The
/// implementation lives beside <see cref="Push.SpinePushRegistration"/> for the same reason.
/// <para>
/// There is no permission method here on purpose. iOS has one notification permission and Android
/// one <c>POST_NOTIFICATIONS</c>, shared by push and local alike, so asking is
/// <see cref="IPushRegistration.RequestPermissionAsync"/>'s job and only its job.
/// </para>
/// </remarks>
public interface INotificationDelivery
{
    /// <summary>False when the platform cannot schedule, so the UI can say so rather than lie.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Makes the device's pending notifications equal <paramref name="plan"/> — anything not in it
    /// is cancelled. Re-planning is therefore idempotent, and a competition that moved or was
    /// unfollowed stops notifying rather than firing from a stale schedule.
    /// </summary>
    Task SyncAsync(IReadOnlyList<PlannedNotification> plan, CancellationToken cancellationToken = default);

    /// <summary>Cancels everything planned. What has already been shown is left alone.</summary>
    Task CancelAllAsync(CancellationToken cancellationToken = default);
}
