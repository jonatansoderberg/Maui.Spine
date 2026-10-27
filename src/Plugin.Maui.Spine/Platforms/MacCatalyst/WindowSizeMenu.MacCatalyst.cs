using CoreGraphics;
using ObjCRuntime;
using UIKit;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The menu a right-click on the header bar opens: the window sizes the page suggests for its content
/// (<see cref="ViewModelBase.SuggestWindowSizes"/>). Built each time it opens, so the sizes follow the content; the
/// size the window has is checked. Choosing one resizes the window with AppKit's animation, keeping its top left
/// corner and staying on the screen. A right-click on a button is the button's.
/// </summary>
internal sealed class WindowSizeMenu(Func<ViewModelBase?> page, SystemInsetsProvider mac) : UIContextMenuInteractionDelegate
{
    /// <summary>
    /// Once, at launch: when the page's content does not fit, gives the window the page's suggested size for its width.
    /// Waits for the window to be adopted and the page to have laid out its content.
    /// </summary>
    public static void FitOnce(View header, Func<ViewModelBase?> page, SystemInsetsProvider mac)
    {
        var tries = 0;
        void Try()
        {
            // Every region tries; the first page with suggestions decides, once per launch.
            if (s_fitted || (tries > 0 && !mac.MacFitWindowToContent)) return;
            if (header.Handler?.PlatformView is UIView { Window: { } window } && mac.MacWindow != IntPtr.Zero && page() is { } vm
                && new WindowSizeMenu(page, mac).Fit(window, vm, onlyGrow: true))
            {
                s_fitted = true;
                return;
            }
            if (++tries < 20)
                header.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), Try);
        }
        // The page fills its content when it appears; give it a moment to lay it out.
        header.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), Try);
    }

    static bool s_fitted;

    /// <summary>
    /// The suggestion for the window's width: the widest no wider than it, or the narrowest. False when the page had no
    /// suggestions (yet).
    /// </summary>
    bool Fit(UIWindow window, ViewModelBase vm, bool onlyGrow)
    {
        var (frame, visible, scale) = Geometry(window);
        if (scale <= 0) return false;
        var sizes = vm.SuggestWindowSizes(new Size((double)visible.Width * scale, (double)visible.Height * scale));
        if (sizes.Count == 0) return false;
        var width = (double)frame.Width * scale;
        var height = (double)frame.Height * scale;
        var size = sizes.Where(s => s.Size.Width <= width + 2).MaxBy(s => s.Size.Width) ?? sizes.MinBy(s => s.Size.Width)!;
        if (onlyGrow ? size.Size.Height > height + 2 : Math.Abs(size.Size.Height - height) > 2 || Math.Abs(size.Size.Width - width) > 2)
            Resize(window, size.Size);
        return true;
    }

    public static void Attach(View header, Func<ViewModelBase?> page, SystemInsetsProvider mac)
    {
        // Every region adds its own; the one showing the page fits the window.
        ViewModelBase.WindowFitter += vm =>
        {
            if (mac.MacWindow != IntPtr.Zero && header.Handler?.PlatformView is UIView { Window: { } window } && ReferenceEquals(page(), vm))
                // After the page has laid out its new content.
                header.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () => new WindowSizeMenu(page, mac).Fit(window, vm, onlyGrow: false));
        };
        void Hook()
        {
            if (header.Handler?.PlatformView is UIView view && !view.Interactions.OfType<UIContextMenuInteraction>().Any(i => i.Delegate is WindowSizeMenu))
                view.AddInteraction(new UIContextMenuInteraction(new WindowSizeMenu(page, mac)));
        }
        header.HandlerChanged += (_, _) => Hook();
        Hook();
    }

    public override UIContextMenuConfiguration? GetConfigurationForMenu(UIContextMenuInteraction interaction, CGPoint location)
    {
        if (interaction.View is not { Window: { } window } view || mac.MacWindow == IntPtr.Zero || page() is not { } vm)
            return null;
        for (var hit = view.HitTest(location, null); hit is not null && hit != view; hit = hit.Superview)
            if (hit is UIControl)
                return null;

        var (frame, visible, scale) = Geometry(window);
        if (scale <= 0) return null;
        var sizes = vm.SuggestWindowSizes(new Size((double)visible.Width * scale, (double)visible.Height * scale));
        if (sizes.Count == 0) return null;

        var current = new Size((double)frame.Width * scale, (double)frame.Height * scale);
        var items = sizes.Select(s =>
        {
            var action = UIAction.Create(s.Title, s.SystemImage is { } name ? UIImage.GetSystemImage(name) : null, null, _ => Resize(window, s.Size));
            action.State = Math.Abs(current.Width - s.Size.Width) < 2 && Math.Abs(current.Height - s.Size.Height) < 2
                ? UIMenuElementState.On : UIMenuElementState.Off;
            return (UIMenuElement)action;
        }).ToArray();
        return UIContextMenuConfiguration.Create(null, null, _ => UIMenu.Create(items));
    }

    // AppKit's frame and the screen's visible frame, and how many UIKit points an AppKit point is: an app scaled to
    // match iPad lays UIKit out in larger points.
    (CGRect Frame, CGRect Visible, double Scale) Geometry(UIWindow window)
    {
        var frame = AppKitObjC.CGRect_msgSend(mac.MacWindow, Selector.GetHandle("frame"));
        var screen = AppKitObjC.IntPtr_msgSend(mac.MacWindow, Selector.GetHandle("screen"));
        var visible = screen == IntPtr.Zero ? frame : AppKitObjC.CGRect_msgSend(screen, Selector.GetHandle("visibleFrame"));
        return (frame, visible, frame.Height > 0 ? (double)(window.Bounds.Height / frame.Height) : 0);
    }

    void Resize(UIWindow window, Size size)
    {
        var (frame, visible, scale) = Geometry(window);
        var width = Math.Min(size.Width / scale, (double)visible.Width);
        var height = Math.Min(size.Height / scale, (double)visible.Height);
        // AppKit's origin is the bottom left: keep the top edge where it is.
        var x = Math.Clamp((double)frame.X, (double)visible.X, (double)(visible.X + visible.Width) - width);
        var y = Math.Clamp((double)(frame.Y + frame.Height) - height, (double)visible.Y, (double)(visible.Y + visible.Height) - height);
        AppKitObjC.Void_msgSend_CGRect_bool_bool(mac.MacWindow, Selector.GetHandle("setFrame:display:animate:"),
            new CGRect(x, y, width, height), true, true);
    }
}
