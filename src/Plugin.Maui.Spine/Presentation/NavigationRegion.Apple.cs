#if IOS || MACCATALYST
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

public sealed partial class NavigationRegion
{
    partial void RestrictBackSwipeOnPlatform()
    {
        _contentHostFront.HandlerChanged += (_, _) => RestrictBackSwipe();
        _contentHostFront.Loaded += (_, _) => RestrictBackSwipe();
    }

    /// <summary>
    /// MAUI's pan is a <see cref="UIPanGestureRecognizer"/> that recognizes a drag in any direction
    /// after a few points and then cancels the touches of the view under it, so a page whose content
    /// handles touches itself lost them a moment into any slow drag. The back-swipe only wants a
    /// rightward drag from the leading edge while there is something to go back to; for anything
    /// else the recognizer is told not to begin, and the content keeps its touches.
    /// </summary>
    private void RestrictBackSwipe()
    {
        if (_contentHostFront.Handler?.PlatformView is not UIView view)
            return;

        foreach (var recognizer in view.GestureRecognizers ?? [])
        {
            if (recognizer is not UIPanGestureRecognizer pan)
                continue;

            pan.ShouldBegin = _ =>
            {
                if (!_dragAccepted || BindingContext is not NavigationRegionViewModel vm || !vm.BackEnabled())
                    return false;

                var translation = pan.TranslationInView(view);
                return translation.X > 0 && translation.X > Math.Abs(translation.Y);
            };
        }
    }
}
#endif
