#if IOS || MACCATALYST

using UIKit;

namespace Plugin.Maui.Spine.Controls;

internal sealed partial class HighlightState
{
    UITapGestureRecognizer? _platformTapRecognizer;
    bool _hostWasInteractive;

    /// <summary>
    /// A tap recogniser that cancels nothing and recognises together with every other, so the view's own
    /// tap handling (Tap's press recogniser, a control inside) runs as before.
    /// </summary>
    partial void WatchPlatformTapCore(bool watch, ref bool handled)
    {
        handled = true;

        if (_platformTapRecognizer is { } old)
        {
            if (old.View is { } previous)
            {
                previous.RemoveGestureRecognizer(old);
                previous.UserInteractionEnabled = _hostWasInteractive;
            }

            _platformTapRecognizer = null;
        }

        if (!watch || _view.Handler?.PlatformView is not UIView host)
            return;

        var recognizer = new UITapGestureRecognizer(() => OnTapped(this, EventArgs.Empty))
        {
            CancelsTouchesInView = false,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false,
            ShouldRecognizeSimultaneously = static (_, _) => true,
        };
        _hostWasInteractive = host.UserInteractionEnabled;
        host.UserInteractionEnabled = true;
        host.AddGestureRecognizer(recognizer);
        _platformTapRecognizer = recognizer;
    }
}

#endif
