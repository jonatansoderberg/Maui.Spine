using Plugin.Maui.Spine.Presentation;
using UIKit;

namespace Plugin.Maui.Spine.Core;

public partial class SpineApplication<TNavigable> where TNavigable : INavigable
{
    partial void HookIosPlatform(Window window)
    {
        var provider = _services.GetRequiredService<ISystemInsetsProvider>() as SystemInsetsProvider;
        if (provider is null) return;

        // Read insets once the window is active (safe area is finalised by then), and again whenever
        // the window's safe area changes. The device's orientation notification is too early for that:
        // it comes before UIKit has turned the interface, and could leave the insets of the orientation
        // being left behind.
        window.Activated += (_, _) =>
        {
            if (window.Handler?.PlatformView is UIWindow uiWindow)
                SafeAreaObserver.Attach(uiWindow, () => provider.UpdateFromUIWindow(uiWindow));

            provider.UpdateFromUIWindow();
        };
    }
}

/// <summary>An invisible view that fills a window and reports every change to its safe area.</summary>
internal sealed class SafeAreaObserver : UIView
{
    private readonly Action _changed;

    private SafeAreaObserver(UIWindow window, Action changed)
    {
        _changed = changed;
        Frame = window.Bounds;
        AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        UserInteractionEnabled = false;
        Hidden = true;
    }

    internal static void Attach(UIWindow window, Action changed)
    {
        if (window.Subviews.OfType<SafeAreaObserver>().Any())
            return;

        window.InsertSubview(new SafeAreaObserver(window, changed), 0);
    }

    public override void SafeAreaInsetsDidChange()
    {
        base.SafeAreaInsetsDidChange();
        _changed();
    }
}
