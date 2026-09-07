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

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Action || IPlatformApplication.Current?.Services is not { } services) return;

        var options = services.GetRequiredService<SpineWidgetsOptions>();
        Schedule(context, options.BackgroundRefreshInterval);

        var pending = GoAsync();
        Task.Run(async () =>
        {
            try { await Extensions.SpineWidgetsExtensions.RunBackgroundRefreshAsync(services, CancellationToken.None); }
            catch (Exception e) { services.GetRequiredService<ILogger<IWidgetService>>().LogError(e, "Background refresh failed."); }
            finally { pending?.Finish(); }
        });
    }

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
