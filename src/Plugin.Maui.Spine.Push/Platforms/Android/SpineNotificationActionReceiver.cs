using Android.App;
using Android.Content;
using Android.Runtime;
using AndroidX.Core.App;
using Plugin.Maui.Spine.Push.Extensions;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>
/// Runs a button that does not open the app, or a reply, through the app's
/// <see cref="IPushHandler.OnActionAsync"/>. Android starts the app's process for the broadcast if it
/// was not running, the same way the widget button does.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
[Register("plugin/maui/spine/push/SpineNotificationActionReceiver")]
internal sealed class SpineNotificationActionReceiver : BroadcastReceiver
{
    internal const string Action = "plugin.maui.spine.push.NOTIFICATION_ACTION";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Action) return;
        if (intent.GetStringExtra(PushNotifications.ActionExtra) is not { } action) return;
        if (PushNotifications.Read(intent) is not { } message) return;

        var text = AndroidX.Core.App.RemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(PushNotifications.ReplyKey)?.ToString();
        var notificationId = intent.GetIntExtra(PushNotifications.NotificationIdExtra, 0);

        if (IPlatformApplication.Current?.Services is not { } services)
        {
            Android.Util.Log.Warn("Spine.Push", $"Button '{action}' was tapped before the app had started; it was not run.");
            NotificationManagerCompat.From(context).Cancel(notificationId);
            return;
        }

        var pending = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                await SpinePushExtensions.ActionAsync(services, message, action, text);
            }
            finally
            {
                // Taken down either way: a button whose work is done has nothing left to offer, and a
                // reply field keeps spinning until its notification is replaced or removed.
                NotificationManagerCompat.From(context).Cancel(notificationId);
                pending?.Finish();
            }
        });
    }
}
