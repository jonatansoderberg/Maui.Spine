#if IOS || MACCATALYST

using CoreAnimation;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class TapState
{
    // A touch that starts a scroll moves a few points within the first frames; waiting this long
    // before highlighting keeps a flick through a list from flashing every row it starts on.
    const double HighlightDelay = 0.07;
    const double Slop = 10;

    UIView? _host;
    PressRecognizer? _press;
    UIHoverGestureRecognizer? _hover;
    CALayer? _overlay;
    bool _pressed;
    bool _hovered;
    int _pressId;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not UIView host)
            return;

        _host = host;
        host.UserInteractionEnabled = true;

        _press = new PressRecognizer(this);
        host.AddGestureRecognizer(_press);

        _hover = new UIHoverGestureRecognizer(OnHover);
        host.AddGestureRecognizer(_hover);
    }

    partial void DisconnectPlatform()
    {
        if (_host is null)
            return;

        if (_press is not null)
            _host.RemoveGestureRecognizer(_press);
        if (_hover is not null)
            _host.RemoveGestureRecognizer(_hover);

        _overlay?.RemoveFromSuperLayer();
        _overlay = null;
        _press = null;
        _hover = null;
        _host = null;
        _pressed = _hovered = false;
    }

    partial void UpdatePlatform()
    {
        if (!CanExecute)
        {
            _pressed = _hovered = false;
            Paint(animated: false);
        }
    }

    void BeginPress()
    {
        var id = ++_pressId;
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, TimeSpan.FromSeconds(HighlightDelay)), () =>
        {
            if (id != _pressId || _press?.State != UIGestureRecognizerState.Possible || !CanExecute)
                return;

            _pressed = true;
            Paint(animated: false);
        });
    }

    void CancelPress()
    {
        _pressId++;
        _pressed = false;
        Paint(animated: true);
    }

    void EndPress()
    {
        _pressId++;

        // A quick tap ends before the delayed highlight; flash it so the tap is still seen.
        _pressed = true;
        Paint(animated: false);

        var id = _pressId;
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, TimeSpan.FromSeconds(0.1)), () =>
        {
            if (id != _pressId)
                return;

            _pressed = false;
            Paint(animated: true);
        });

        Execute();
    }

    void OnHover(UIHoverGestureRecognizer recognizer)
    {
        _hovered = recognizer.State is UIGestureRecognizerState.Began or UIGestureRecognizerState.Changed
            && CanExecute;
        Paint(animated: true);
    }

    void Paint(bool animated)
    {
        if (_host is not { } host)
            return;

        var visible = _pressed || _hovered;
        if (!visible && _overlay is null)
            return;

        if (_overlay is null)
        {
            // Above every subview's layer, whatever order MAUI adds them in.
            _overlay = new CALayer { ZPosition = 10_000, Opacity = 0 };
            host.Layer.AddSublayer(_overlay);
        }

        CATransaction.Begin();
        CATransaction.DisableActions = !animated;
        CATransaction.AnimationDuration = 0.2;

        if (visible)
        {
            var color = HighlightColor?.ToPlatform()
                ?? (_pressed ? UIColor.SystemFill : UIColor.QuaternarySystemFill);
            var resolved = color.GetResolvedColor(host.TraitCollection);

            _overlay.Frame = host.Bounds;
            _overlay.CornerRadius = (nfloat)CornerRadius();
            _overlay.BackgroundColor = resolved.CGColor;
            // A custom colour is drawn as given when pressed and at half strength on hover.
            _overlay.Opacity = HighlightColor is not null && !_pressed ? 0.5f : 1f;
        }
        else
        {
            _overlay.Opacity = 0;
        }

        CATransaction.Commit();
    }

    /// <summary>
    /// Tracks one finger on the view: highlights while it rests, gives up when it moves (a scroll),
    /// and runs the command when it lifts. It recognises together with every other recogniser so a
    /// surrounding scroll view keeps scrolling.
    /// </summary>
    sealed class PressRecognizer : UIGestureRecognizer
    {
        readonly WeakReference<TapState> _owner;
        CGPoint _start;

        public PressRecognizer(TapState owner)
        {
            _owner = new(owner);
            CancelsTouchesInView = false;
            DelaysTouchesEnded = false;
            ShouldRecognizeSimultaneously = static (_, _) => true;
            ShouldReceiveTouch = static (recognizer, touch) => ReceivesTouch(recognizer, touch);
        }

        TapState? Owner => _owner.TryGetTarget(out var owner) ? owner : null;

        // A control in the view (a switch in a row) and a nested tap target own their touches.
        static bool ReceivesTouch(UIGestureRecognizer recognizer, UITouch touch)
        {
            for (var view = touch.View; view is not null && view != recognizer.View; view = view.Superview)
            {
                if (view is UIControl)
                    return false;

                if (view.GestureRecognizers?.Any(static r => r is PressRecognizer) == true)
                    return false;
            }

            return true;
        }

        public override void TouchesBegan(NSSet touches, UIEvent evt)
        {
            base.TouchesBegan(touches, evt);

            if (Owner is not { } owner || touches.Count != 1 || NumberOfTouches > 1 || !owner.CanExecute
                || touches.AnyObject is not UITouch touch)
            {
                State = UIGestureRecognizerState.Failed;
                return;
            }

            _start = touch.LocationInView(View);
            owner.BeginPress();
        }

        public override void TouchesMoved(NSSet touches, UIEvent evt)
        {
            base.TouchesMoved(touches, evt);

            if (State != UIGestureRecognizerState.Possible || touches.AnyObject is not UITouch touch)
                return;

            var point = touch.LocationInView(View);
            if (Math.Abs(point.X - _start.X) > Slop || Math.Abs(point.Y - _start.Y) > Slop)
            {
                Owner?.CancelPress();
                State = UIGestureRecognizerState.Failed;
            }
        }

        public override void TouchesEnded(NSSet touches, UIEvent evt)
        {
            base.TouchesEnded(touches, evt);

            if (State != UIGestureRecognizerState.Possible)
                return;

            State = UIGestureRecognizerState.Recognized;
            Owner?.EndPress();
        }

        public override void TouchesCancelled(NSSet touches, UIEvent evt)
        {
            base.TouchesCancelled(touches, evt);

            if (State != UIGestureRecognizerState.Possible)
                return;

            Owner?.CancelPress();
            State = UIGestureRecognizerState.Failed;
        }
    }
}

public static partial class Semantic
{
    // UIAccessibilityTraitToggleButton (iOS 17) is not bound; read the constant from UIKit. 0 before iOS 17.
    // RTLD_DEFAULT (-2) searches every loaded image, UIKit's path differs on Mac Catalyst.
    static readonly Lazy<UIAccessibilityTrait> ToggleButtonTrait = new(static () =>
        (UIAccessibilityTrait)ObjCRuntime.Dlfcn.GetInt64(new IntPtr(-2), "UIAccessibilityTraitToggleButton"));

    static partial void ApplyPlatform(View view, bool merged, bool button, bool? toggled, bool enabled)
    {
        if (view.Handler?.PlatformView is not UIView host)
            return;

        if (merged)
            host.IsAccessibilityElement = true;

        var toggleTrait = ToggleButtonTrait.Value;
        var ours = UIAccessibilityTrait.Button | UIAccessibilityTrait.NotEnabled | toggleTrait;
        var wanted = (UIAccessibilityTrait)0;

        if (button || toggled is not null)
            wanted |= UIAccessibilityTrait.Button;

        if (!enabled)
            wanted |= UIAccessibilityTrait.NotEnabled;

        // VoiceOver speaks a toggle button's "1" and "0" as on and off, in the user's language.
        if (toggled is not null && toggleTrait != 0)
        {
            wanted |= toggleTrait;
            host.AccessibilityValue = toggled.Value ? "1" : "0";
        }
        else if (host.AccessibilityValue is "1" or "0")
        {
            host.AccessibilityValue = null;
        }

        host.AccessibilityTraits = (host.AccessibilityTraits & ~ours) | wanted;

        // A CollectionView without selection clears the button trait of its cells' content when
        // it binds them, after the content's handlers ran; put it back once the bind is done.
        if (host.Superview?.Superview is UICollectionViewCell)
            DispatchQueue.MainQueue.DispatchAsync(() => host.AccessibilityTraits = (host.AccessibilityTraits & ~ours) | wanted);
    }
}

#endif
