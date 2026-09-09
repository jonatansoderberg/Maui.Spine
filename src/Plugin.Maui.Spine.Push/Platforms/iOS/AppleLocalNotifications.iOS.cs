using Foundation;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using UserNotifications;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>
/// Local notifications on iOS and Mac Catalyst. The pending set is replaced wholesale rather than
/// diffed: the plan is cheap to rebuild, and a diff would be one more place a stale schedule could
/// survive.
/// </summary>
/// <remarks>
/// The foreground presentation is not decided here. Spine.Push owns
/// <c>UNUserNotificationCenter.Current.Delegate</c> and the system hands it local notifications too,
/// so the app's <see cref="IPushHandler"/> is asked what to show — the same answer it gives for a
/// push.
/// </remarks>
internal sealed class AppleLocalNotifications : ILocalNotificationService
{
    /// <summary>
    /// Apple keeps at most this many pending notifications per app and silently drops the rest, so
    /// the nearest ones are the ones scheduled.
    /// </summary>
    private const int Limit = 64;

    public bool IsSupported => true;

    public async Task SyncAsync(IEnumerable<LocalNotification> plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var center = UNUserNotificationCenter.Current;
        center.RemoveAllPendingNotificationRequests();

        var now = DateTimeOffset.Now;
        var due = plan.Where(n => n.At > now).OrderBy(n => n.At).ToList();

        if (due.Count > Limit)
        {
            Logger?.LogWarning(
                "Spine.Push: {Count} local notifications planned, but iOS keeps {Limit}. The {Limit} nearest were scheduled.",
                due.Count, Limit, Limit);

            due = [.. due.Take(Limit)];
        }

        foreach (var notification in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = new UNMutableNotificationContent
            {
                Title = notification.Title,
                Body = notification.Body ?? "",
                Sound = UNNotificationSound.Default,
                UserInfo = UserInfo(notification),
            };

            // The thread groups a notification with its neighbours in the Notification Center, which
            // is what a channel means on this side.
            if (notification.Channel is { Length: > 0 } channel) content.ThreadIdentifier = channel;

            // An interval from now, not calendar components: At is an absolute instant, and a
            // calendar trigger would fire at that wall clock in whichever time zone the device is in
            // when the moment comes — the wrong answer for someone who travelled since planning.
            var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(
                Math.Max((notification.At - DateTimeOffset.Now).TotalSeconds, 1), repeats: false);

            var request = UNNotificationRequest.FromIdentifier(notification.Id, content, trigger);

            try
            {
                await center.AddNotificationRequestAsync(request);
            }
            catch (NSErrorException e)
            {
                Logger?.LogWarning("Spine.Push: scheduling {Id} failed: {Error}", notification.Id, e.Error.LocalizedDescription);
            }
        }
    }

    public async Task<IReadOnlyList<LocalNotification>> PendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await UNUserNotificationCenter.Current.GetPendingNotificationRequestsAsync();

        return [.. pending.Select(Read).OfType<LocalNotification>().OrderBy(n => n.At)];
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default)
    {
        UNUserNotificationCenter.Current.RemoveAllPendingNotificationRequests();
        return Task.CompletedTask;
    }

    private static NSDictionary UserInfo(LocalNotification notification)
    {
        var data = LocalNotificationPayload.Write(notification);

        return NSDictionary.FromObjectsAndKeys(
            [.. data.Values.Select(NSObject (v) => new NSString(v))],
            [.. data.Keys.Select(NSObject (k) => new NSString(k))]);
    }

    /// <summary>
    /// Turns a pending request back into the notification the app planned. The system is the truth
    /// here, so what it still holds is what is reported — including anything scheduled by an earlier
    /// run of the app.
    /// </summary>
    private static LocalNotification? Read(UNNotificationRequest request)
    {
        if (request.Trigger is not UNTimeIntervalNotificationTrigger) return null;

        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in request.Content.UserInfo)
        {
            if (key.ToString() is { } name) data[name] = value?.ToString() ?? "";
        }

        // The instant comes from the payload, not from the trigger: NextTriggerDate on an interval
        // trigger answers now plus the interval, so a pending notification would appear to drift
        // further away every time the plan was read.
        if (!long.TryParse(data.GetValueOrDefault(LocalNotificationPayload.At), out var unix)) return null;

        return new LocalNotification
        {
            Id = request.Identifier,
            At = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime(),
            Title = request.Content.Title,
            Body = request.Content.Body is { Length: > 0 } body ? body : null,
            Route = data.GetValueOrDefault(PushKeys.Route),
            Channel = data.GetValueOrDefault(PushKeys.Channel),
            Data = LocalNotificationPayload.App(data),
        };
    }

    private static ILogger? Logger =>
        IPlatformApplication.Current?.Services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Push");
}
