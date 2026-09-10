using Orientera.Services.Notifications;
using Plugin.Maui.Spine.Push;

namespace Orientera.Services.Push;

/// <summary>
/// Hands the plan to Spine, which schedules it. Kept out of <c>Services/Notifications</c> for the
/// same reason as <see cref="SpinePushRegistration"/>: that folder is compiled into the tests on
/// plain .NET, where the MAUI package does not exist.
/// </summary>
public sealed class SpineNotificationDelivery(ILocalNotificationService _local) : INotificationDelivery
{
    /// <summary>The channel push already uses. One "Tävlingar" in the system settings, not two.</summary>
    private const string Channel = "competitions";

    public bool IsSupported => _local.IsSupported;

    public Task SyncAsync(IReadOnlyList<PlannedNotification> plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return _local.SyncAsync([.. plan.Select(Notification)], cancellationToken);
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default) =>
        _local.CancelAllAsync(cancellationToken);

    /// <summary>
    /// The route is what makes a locally scheduled notification open the same page a pushed one
    /// does — <see cref="OrienteraPushHandler"/> reads it without caring which half sent it.
    /// </summary>
    private static LocalNotification Notification(PlannedNotification planned) => new()
    {
        Id = planned.Id,
        At = planned.At,
        Title = planned.Title,
        Body = planned.Body,
        Route = PushRoute.For(planned.Kind, planned.Competition).ToString(),
        Channel = Channel,
    };
}
