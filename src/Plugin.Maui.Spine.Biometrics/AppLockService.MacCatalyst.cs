#if MACCATALYST
using Foundation;
using UIKit;

namespace Plugin.Maui.Spine.Biometrics;

internal sealed partial class AppLockService
{
    /// <remarks>
    /// MAUI follows the application's lifecycle here, and the application stays active while its window is
    /// minimised; only the window's scene goes to the background. On a cold start the window has no scene
    /// yet, so the scene is matched when a notification comes: another scene goes to the background at launch.
    /// </remarks>
    partial void WatchMinimised(Window window)
    {
        bool Ours(NSNotificationEventArgs args) =>
            args.Notification.Object is UIScene scene && window.Handler?.PlatformView is UIWindow { WindowScene: { } own } && own == scene;

        UIScene.Notifications.ObserveDidEnterBackground((_, args) =>
        {
            if (Ours(args))
                OnStopped(null, EventArgs.Empty);
        });
        UIScene.Notifications.ObserveWillEnterForeground((_, args) =>
        {
            if (Ours(args))
                OnResumed(null, EventArgs.Empty);
        });
    }
}
#endif
