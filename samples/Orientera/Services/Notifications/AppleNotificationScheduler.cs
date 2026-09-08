#if IOS || MACCATALYST
using Foundation;
using UserNotifications;

namespace Orientera.Services.Notifications;

/// <summary>
/// Local notifications on iOS and Mac Catalyst.
/// </summary>
/// <remarks>
/// Syncing replaces the pending set wholesale rather than diffing it: the plan is cheap to
/// rebuild, and a diff would be one more place for a stale schedule to survive.
///
/// The foreground presentation is not set here. Spine.Push owns
/// <c>UNUserNotificationCenter.Current.Delegate</c>, and the system hands it local notifications
/// too — <c>OrienteraPushHandler</c> is what decides what a notification looks like while the app
/// is open, whether it came from this scheduler or from the backend.
/// </remarks>
public sealed class AppleNotificationScheduler : INotificationScheduler
{
    public bool IsSupported => true;

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        var (granted, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);

        return granted;
    }

    public async Task SyncAsync(IReadOnlyList<PlannedNotification> plan, CancellationToken cancellationToken = default)
    {
        var center = UNUserNotificationCenter.Current;

        center.RemoveAllPendingNotificationRequests();

        foreach (var notification in plan)
        {
            var content = new UNMutableNotificationContent
            {
                Title = notification.Title,
                Body = notification.Body,
                Sound = UNNotificationSound.Default,
            };

            var local = notification.At.ToLocalTime();

            var components = new NSDateComponents
            {
                Year = local.Year,
                Month = local.Month,
                Day = local.Day,
                Hour = local.Hour,
                Minute = local.Minute,
                Second = local.Second,
            };

            var request = UNNotificationRequest.FromIdentifier(
                notification.Id,
                content,
                UNCalendarNotificationTrigger.CreateTrigger(components, repeats: false));

            await center.AddNotificationRequestAsync(request);
        }
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default)
    {
        UNUserNotificationCenter.Current.RemoveAllPendingNotificationRequests();
        return Task.CompletedTask;
    }
}
#endif
