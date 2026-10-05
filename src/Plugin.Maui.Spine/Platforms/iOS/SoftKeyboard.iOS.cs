using Foundation;
using UIKit;

namespace Plugin.Maui.Spine.Core;

internal static partial class SoftKeyboard
{
    private static NSObject? _observer;

    /// <summary>The curve of the keyboard's last move, for animating along with it.</summary>
    public static UIViewAnimationCurve Curve { get; private set; }

    static partial void Observe()
    {
        _observer ??= UIKeyboard.Notifications.ObserveWillChangeFrame((_, e) =>
        {
            var frame = e.FrameEnd;
            var screen = UIScreen.MainScreen.Bounds;

            // A keyboard that does not reach the bottom of the screen is floating or undocked (iPad):
            // it covers a spot the page cannot move away from, so it covers nothing, as UIKit's
            // keyboard layout guide treats it. Off screen it is down.
            var docked = frame.Height > 0 && frame.Top < screen.Bottom && frame.Bottom >= screen.Bottom - 1;

            Curve = e.AnimationCurve;
            Report(docked ? frame.Top : null, TimeSpan.FromSeconds(e.AnimationDuration));
        });
    }
}
