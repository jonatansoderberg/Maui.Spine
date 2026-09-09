using Android.App;
using Android.Content;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>
/// Local notifications on Android: an alarm per planned notification that wakes
/// <see cref="SpineLocalNotificationReceiver"/> in the app's process, which posts it through the same
/// builder push uses.
/// </summary>
/// <remarks>
/// The alarms are inexact on purpose. Exact ones need <c>SCHEDULE_EXACT_ALARM</c>, which Android
/// hands out for alarm clocks and calendar events; none of these are worth being wrong by a few
/// minutes, they are worth arriving at all.
/// </remarks>
internal sealed class AndroidLocalNotifications : ILocalNotificationService
{
    public bool IsSupported => true;

    public Task SyncAsync(IEnumerable<LocalNotification> plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var now = DateTimeOffset.Now;
        Schedule([.. plan.Where(n => n.At > now).OrderBy(n => n.At)]);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LocalNotification>> PendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return Task.FromResult<IReadOnlyList<LocalNotification>>([.. LocalNotificationStore.Read().Where(n => n.At > now)]);
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default) => SyncAsync([], cancellationToken);

    /// <summary>
    /// Cancels what the store knows about and books what the plan says. Written down before it is
    /// booked: an alarm nobody remembers cannot be cancelled, while a remembered alarm that was never
    /// set costs one pointless cancel.
    /// </summary>
    internal static void Schedule(IReadOnlyList<LocalNotification> plan)
    {
        var context = Platform.AppContext;
        var alarms = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarms is null) return;

        foreach (var stale in LocalNotificationStore.Read())
            alarms.Cancel(Pending(context, stale, forCancel: true));

        LocalNotificationStore.Write(plan);

        foreach (var notification in plan)
            alarms.Set(AlarmType.RtcWakeup, notification.At.ToUnixTimeMilliseconds(), Pending(context, notification, forCancel: false));
    }

    /// <summary>
    /// The alarm's intent. Cancelling matches on everything but the extras, so the cancelling one
    /// carries none — and the request code is a stable hash of the id, because
    /// <see cref="string.GetHashCode()"/> is salted per process and would not match the code an
    /// earlier run used.
    /// </summary>
    private static PendingIntent Pending(Context context, LocalNotification notification, bool forCancel)
    {
        var intent = new Intent(context, typeof(SpineLocalNotificationReceiver))
            .SetAction(SpineLocalNotificationReceiver.Action);

        if (!forCancel)
        {
            foreach (var (key, value) in LocalNotificationPayload.Write(notification)) intent.PutExtra(key, value);
        }

        var flags = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;

        return PendingIntent.GetBroadcast(context, PushNotifications.StableId(notification.Id), intent, flags)!;
    }
}
