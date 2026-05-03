using Foundation;
using Plugin.Maui.Spine.Presentation;
using UIKit;

namespace Plugin.Maui.Spine.Core;

public partial class SpineApplication<TNavigable> where TNavigable : INavigable
{
    partial void HookIosPlatform(Window window)
    {
        var provider = _services.GetRequiredService<ISystemInsetsProvider>() as SystemInsetsProvider;
        if (provider is null) return;

        // Read insets once the window is active (safe area is finalised by then).
        window.Activated += (_, _) => provider.UpdateFromUIWindow();

        // Re-read on device rotation — safe area changes with orientation.
        NSNotificationCenter.DefaultCenter.AddObserver(
            UIDevice.OrientationDidChangeNotification,
            _ => MainThread.BeginInvokeOnMainThread(provider.UpdateFromUIWindow));
    }
}
