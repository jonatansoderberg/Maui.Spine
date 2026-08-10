#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace Orientera.Services.Notifications;

/// <summary>
/// Local notifications on Android: one channel, and an alarm per planned notification that
/// wakes a receiver which posts it.
/// </summary>
/// <remarks>
/// The alarms are inexact on purpose. Exact alarms need <c>SCHEDULE_EXACT_ALARM</c>, which
/// Android hands out for alarm clocks and calendar events, and none of these notifications are
/// worth being wrong by a few minutes — they are worth arriving at all.
/// </remarks>
public sealed class AndroidNotificationScheduler : INotificationScheduler
{
    internal const string ChannelId = "orientera.competition";
    internal const string TitleExtra = "title";
    internal const string BodyExtra = "body";
    internal const string IdExtra = "id";

    private readonly List<string> _scheduled = [];

    public bool IsSupported => true;

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        EnsureChannel();

        // Below API 33 a posted notification needs no runtime permission.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return true;

        var status = await Permissions.RequestAsync<Permissions.PostNotifications>();

        return status == PermissionStatus.Granted;
    }

    public Task SyncAsync(IReadOnlyList<PlannedNotification> plan, CancellationToken cancellationToken = default)
    {
        EnsureChannel();

        var context = Platform.AppContext;
        var alarms = (AlarmManager)context.GetSystemService(Android.Content.Context.AlarmService)!;

        foreach (var id in _scheduled)
            alarms.Cancel(PendingIntentFor(context, id, null));

        _scheduled.Clear();

        foreach (var notification in plan)
        {
            var pending = PendingIntentFor(context, notification.Id, notification);

            alarms.Set(AlarmType.RtcWakeup, notification.At.ToUnixTimeMilliseconds(), pending);
            _scheduled.Add(notification.Id);
        }

        return Task.CompletedTask;
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default) =>
        SyncAsync([], cancellationToken);

    private static PendingIntent PendingIntentFor(Android.Content.Context context, string id, PlannedNotification? notification)
    {
        var intent = new Intent(context, typeof(NotificationReceiver));
        intent.PutExtra(IdExtra, id);

        if (notification is not null)
        {
            intent.PutExtra(TitleExtra, notification.Title);
            intent.PutExtra(BodyExtra, notification.Body);
        }

        // API 31+ rejects a mutable PendingIntent outright.
        var flags = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;

        return PendingIntent.GetBroadcast(context, id.GetHashCode(), intent, flags)!;
    }

    private static void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var manager = (NotificationManager)Platform.AppContext.GetSystemService(Android.Content.Context.NotificationService)!;

        if (manager.GetNotificationChannel(ChannelId) is not null)
            return;

        manager.CreateNotificationChannel(new NotificationChannel(
            ChannelId,
            "Tävlingar",
            NotificationImportance.Default)
        {
            Description = "Anmälan, PM, starttid, live och resultat.",
        });
    }
}

/// <summary>Posts the notification when its alarm fires.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class NotificationReceiver : BroadcastReceiver
{
    public override void OnReceive(Android.Content.Context? context, Intent? intent)
    {
        if (context is null || intent is null)
            return;

        var title = intent.GetStringExtra(AndroidNotificationScheduler.TitleExtra);
        var body = intent.GetStringExtra(AndroidNotificationScheduler.BodyExtra);
        var id = intent.GetStringExtra(AndroidNotificationScheduler.IdExtra) ?? string.Empty;

        if (title is null || body is null)
            return;

        var builder = new NotificationCompat.Builder(context, AndroidNotificationScheduler.ChannelId)
            .SetContentTitle(title)!
            .SetContentText(body)!
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))!
            .SetSmallIcon(Resource.Drawable.notification_icon)!
            .SetAutoCancel(true)!;

        NotificationManagerCompat.From(context).Notify(id.GetHashCode(), builder.Build());
    }
}
#endif
