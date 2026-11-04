using Android.App;
using Android.Content;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// The app's background run on Android: an inexact alarm, booked when the app goes to the background
/// and again after each run, that wakes this receiver in the app's process to run the
/// <see cref="IBackgroundRefreshHandler"/> and rebuild the widgets. The same mechanism the widget
/// receivers use for <c>Refresh(after)</c>, so no WorkManager dependency.
/// </summary>
[Register("plugin/maui/spine/widgets/SpineBackgroundReceiver")]
internal sealed class SpineBackgroundReceiver : BroadcastReceiver
{
    private const string Action = "plugin.maui.spine.widgets.BACKGROUND_REFRESH";
    private const string DismissedAction = "plugin.maui.spine.widgets.ACTIVITY_DISMISSED";
    private const string ActivityButtonAction = "plugin.maui.spine.widgets.ACTIVITY_BUTTON";
    private const string ExtraKind = "plugin.maui.spine.widgets.KIND";
    private const string ExtraAction = "plugin.maui.spine.widgets.ACTION";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || IPlatformApplication.Current?.Services is not { } services) return;

        if (intent?.Action == DismissedAction)
        {
            if (services.GetService<ILiveActivityService>() is LiveActivityService activities) activities.Reconcile();
            return;
        }

        if (intent?.Action == ActivityButtonAction && intent.GetStringExtra(ExtraKind) is { } kind && intent.GetStringExtra(ExtraAction) is { } actionId)
        {
            var at = DateTimeOffset.UtcNow;
            var log = services.GetRequiredService<ILogger<IWidgetService>>();
            ReceiverWork.Run(GoAsync(),
                cancellationToken => Extensions.SpineWidgetsExtensions.HandleActionAsync(services, kind, actionId, at, cancellationToken),
                e => log.LogError(e, "Live Activity action \"{Action}\" failed.", actionId),
                _ => log.LogWarning("Live Activity action \"{Action}\" outlived the broadcast.", actionId));
            return;
        }

        if (intent?.Action != Action) return;
        // An alarm an earlier version booked; Plugin.Maui.Spine.BackgroundTasks runs the refresh now.
        if (Extensions.SpineWidgetsExtensions.BackgroundTasksOwnRefresh(services)) return;

        var options = services.GetRequiredService<SpineWidgetsOptions>();
        Schedule(context, options.BackgroundRefreshInterval);

        var logger = services.GetRequiredService<ILogger<IWidgetService>>();
        var handler = options.BackgroundRefreshHandler?.Name ?? "the widget refresh";
        ReceiverWork.Run(GoAsync(),
            cancellationToken => Extensions.SpineWidgetsExtensions.RunBackgroundRefreshAsync(services, cancellationToken),
            e => logger.LogError(e, "Background refresh failed."),
            stopped =>
            {
                if (stopped) logger.LogWarning("Background refresh used its {Budget} s and was cancelled in {Handler}.", ReceiverWork.Budget.TotalSeconds, handler);
                else logger.LogWarning("Background refresh was still running in {Handler} after {Deadline} s and did not stop on its cancellation token; the broadcast was finished without it.", handler, ReceiverWork.Deadline.TotalSeconds);
            });
    }

    /// <summary>The intent a button of a Live Activity's notification fires: its kind and action, one per pair.</summary>
    internal static PendingIntent ActivityButton(Context context, string kind, string actionId) =>
        PendingIntent.GetBroadcast(context, 2000 + ((kind + "|" + actionId).GetHashCode() & 0xFFFF),
            new Intent(context, typeof(SpineBackgroundReceiver)).SetAction(ActivityButtonAction).PutExtra(ExtraKind, kind).PutExtra(ExtraAction, actionId),
            OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable : PendingIntentFlags.UpdateCurrent)!;

    /// <summary>The intent a Live Activity's notification fires when the user swipes it away.</summary>
    internal static PendingIntent ActivityDismissed(Context context) =>
        PendingIntent.GetBroadcast(context, 1, new Intent(context, typeof(SpineBackgroundReceiver)).SetAction(DismissedAction),
            OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable : PendingIntentFlags.UpdateCurrent)!;

    internal static void Schedule(Context context, TimeSpan interval)
    {
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        var pending = PendingIntent.GetBroadcast(context, 0, new Intent(context, typeof(SpineBackgroundReceiver)).SetAction(Action),
            OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable : PendingIntentFlags.UpdateCurrent)!;

        if (interval <= TimeSpan.Zero)
        {
            alarms.Cancel(pending);
            return;
        }

        var at = DateTimeOffset.UtcNow.Add(interval).ToUnixTimeMilliseconds();
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
        else alarms.Set(AlarmType.RtcWakeup, at, pending);
    }
}
