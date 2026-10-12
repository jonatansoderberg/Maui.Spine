#if IOS || MACCATALYST
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;

namespace Plugin.Maui.Spine.Core;

public static partial class SpineOverlay
{
    /// <summary>Puts the overlays back on top after a sheet's presentation has gone into the window over them.</summary>
    internal static void Raise()
    {
        foreach (var handle in _shown)
            handle.Raise();
    }

    private sealed partial class Handle
    {
        private UIView? _view;

        /// <remarks>A view on the app's window, as <c>LightboxOverlay</c> uses: a sheet is a presentation in the same window.</remarks>
        public partial void Attach(IMauiContext context)
        {
            if (_window.Handler?.PlatformView is not UIWindow platformWindow)
                return;

            var view = _content.ToPlatform(context);
            view.RemoveFromSuperview();
            view.Frame = platformWindow.Bounds;
            view.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
            view.AccessibilityViewIsModal = true;
            platformWindow.AddSubview(view);
            _view = view;

            // Before the window is shown its root view is not in it yet, and goes in on top.
            if (platformWindow.Hidden)
            {
                NSObject? observer = null;
                observer = UIWindow.Notifications.ObserveDidBecomeVisible(platformWindow, (_, _) =>
                {
                    observer?.Dispose();
                    Raise();
                });
            }

            UIAccessibility.PostNotification(UIAccessibilityPostNotification.ScreenChanged, view);
        }

        public void Raise()
        {
            if (_view?.Superview is { } superview)
                superview.BringSubviewToFront(_view);
        }

        private partial void Detach()
        {
            _view?.RemoveFromSuperview();
            _view = null;

            UIAccessibility.PostNotification(UIAccessibilityPostNotification.ScreenChanged, null);
        }
    }
}
#endif
