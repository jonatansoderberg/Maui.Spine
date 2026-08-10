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
/// </remarks>
public sealed class AppleNotificationScheduler : INotificationScheduler
{
    public AppleNotificationScheduler() =>
        UNUserNotificationCenter.Current.Delegate = new ForegroundPresenter();

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

    /// <summary>
    /// Without a delegate iOS hides notifications that arrive while the app is open — which is
    /// exactly when "dags att åka" matters most.
    /// </summary>
    private sealed class ForegroundPresenter : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(
            UNUserNotificationCenter center,
            UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler) =>
            completionHandler(OperatingSystem.IsIOSVersionAtLeast(14)
                ? UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound
                : UNNotificationPresentationOptions.Alert | UNNotificationPresentationOptions.Sound);
    }
}
#endif
