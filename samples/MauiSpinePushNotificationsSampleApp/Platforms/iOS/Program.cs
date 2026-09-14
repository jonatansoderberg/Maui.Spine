using Plugin.Maui.Spine.PushNotifications;
using UIKit;

namespace MauiSpinePushNotificationsSampleApp;

public class Program
{
    static void Main(string[] args)
    {
        // Spine.PushNotifications adds the remote-notification delegate methods to the AppDelegate class here,
        // because UIKit reads which callbacks the delegate implements when UIApplication.Main assigns
        // it. See docs/wiki/push-notifications.md.
        SpinePushNotifications.Install();

        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
