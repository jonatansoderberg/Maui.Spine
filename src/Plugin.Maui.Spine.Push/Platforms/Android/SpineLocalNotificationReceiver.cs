using Android.App;
using Android.Content;
using Android.Runtime;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Push.Extensions;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>Posts a planned notification when its alarm goes off.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
[Register("plugin/maui/spine/push/SpineLocalNotificationReceiver")]
internal sealed class SpineLocalNotificationReceiver : BroadcastReceiver
{
    internal const string Action = "plugin.maui.spine.push.LOCAL_NOTIFICATION";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Action) return;
        if (PushNotifications.Read(intent) is not { } message) return;

        LocalNotificationStore.Remove(message.Data.GetValueOrDefault(PushKeys.Collapse) ?? "");

        var pending = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                var presentation = SpinePushExtensions.DefaultPresentation;

                // In the foreground the app's handler decides what is shown, exactly as it does for a
                // push — that is what WillPresentNotification gives the app on Apple. In the background
                // it is not asked, for the same reason: Apple does not ask either, and the two should
                // behave alike.
                if (IPlatformApplication.Current?.Services is { } services && SpinePushLifecycle.IsForeground)
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var reception = new PushContext(true, false, DateTimeOffset.UtcNow, deadline.Token);
                    presentation = await SpinePushExtensions.DeliverAsync(services, message, reception);

                    if (presentation == PushPresentation.None) return;
                }

                var picture = await PushNotifications.PictureAsync(message, CancellationToken.None);
                PushNotifications.Show(context, message, presentation, picture);
            }
            finally { pending?.Finish(); }
        });
    }
}

/// <summary>
/// Books the plan again after a restart. Alarms do not survive one, and an app that notifies about
/// something tomorrow morning has no reason to be opened in between — so without this it simply goes
/// quiet.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
[Register("plugin/maui/spine/push/SpineLocalNotificationBootReceiver")]
internal sealed class SpineLocalNotificationBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted) return;

        var now = DateTimeOffset.Now;
        AndroidLocalNotifications.Schedule([.. LocalNotificationStore.Read().Where(n => n.At > now)]);
    }
}
