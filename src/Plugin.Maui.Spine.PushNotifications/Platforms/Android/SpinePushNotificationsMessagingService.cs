using Android.App;
using Android.Content;
using Firebase.Messaging;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.PushNotifications.Extensions;

namespace Plugin.Maui.Spine.PushNotifications;

/// <summary>
/// Receives Firebase messages. Spine sends data-only, so this runs for every message whether the app
/// is in front or not, and the notification is drawn here rather than by the system.
/// </summary>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class SpinePushNotificationsMessagingService : FirebaseMessagingService
{
    // The binding marks OnNewToken obsolete without a replacement; it is still how FCM reports a new token.
#pragma warning disable CS0618, CS0672
    /// <inheritdoc />
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        // A rotated token is worthless until the backend hears about it; PushNotificationService listens for this.
        AndroidPushPlatform.SetHandle(token);
    }
#pragma warning restore CS0618, CS0672

    /// <inheritdoc />
    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        var data = message.Data.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var push = PushMessage.From(data);

        _ = HandleAsync(push);
    }

    private async Task HandleAsync(PushMessage message)
    {
        // FCM gives a data message about twenty seconds before it may kill the process.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var foreground = IPlatformApplication.Current?.Services is not null && SpinePushNotificationsLifecycle.IsForeground;
        var context = new PushContext(foreground, !foreground, DateTimeOffset.UtcNow, deadline.Token);

        var presentation = SpinePushNotificationsExtensions.DefaultPresentation;

        if (IPlatformApplication.Current?.Services is { } services)
        {
            // Widget rebuilds and Live Updates are Spine's own work; the app's handler still sees them.
            await SpinePushNotificationsExtensions.HandleInternallyAsync(services, message, deadline.Token);
            presentation = await SpinePushNotificationsExtensions.DeliverAsync(services, message, context);
        }

        // Only an alert is ever drawn. The other kinds are the app's business, not the shade's.
        if (message.Kind != PushKind.Alert) return;
        if (foreground && presentation == PushPresentation.None) return;
        if (message.Title is null && message.Body is null) return;

        try
        {
            // Fetched within FCM's budget for the message; a picture that cannot be had leaves the text.
            var picture = await AndroidNotifications.PictureAsync(message, deadline.Token);
            AndroidNotifications.Show(this, message, presentation, picture);
        }
        catch (Exception e)
        {
            IPlatformApplication.Current?.Services.GetService<ILoggerFactory>()
                ?.CreateLogger("Plugin.Maui.Spine.PushNotifications")
                .LogError(e, "Spine.PushNotifications: drawing the notification failed.");
        }
    }
}

/// <summary>Whether the app is in the foreground, which the messaging service cannot ask MAUI directly.</summary>
internal static class SpinePushNotificationsLifecycle
{
    internal static bool IsForeground { get; set; }
}
