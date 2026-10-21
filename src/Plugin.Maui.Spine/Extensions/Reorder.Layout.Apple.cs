#if IOS || MACCATALYST

using CoreAnimation;
using Foundation;
using CoreGraphics;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class LayoutReorderState
{
    UIView? _host;
    UILongPressGestureRecognizer? _press;
    CAShapeLayer? _placeholder;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not UIView view || _press is not null)
            return;

        _host = view;
        _press = new UILongPressGestureRecognizer(Drive)
        {
            ShouldBegin = _ => CanReorder,
#if MACCATALYST
            // A pointer press need not be held as long as a finger, and it may move a little while it is held.
            MinimumPressDuration = 0.1,
            AllowableMovement = 20,
#endif
        };
        view.AddGestureRecognizer(_press);
    }

    partial void DisconnectPlatform()
    {
        if (_dragged is not null)
            _ = EndAsync(cancelled: true);
        if (_press is not null)
        {
            _host?.RemoveGestureRecognizer(_press);
            _press.Dispose();
            _press = null;
        }
        HidePlaceholderPlatform();
        _host = null;
    }

    void Drive(UILongPressGestureRecognizer recognizer)
    {
        if (_host is null)
            return;
        var location = recognizer.LocationInView(_host);
        var point = new Point(location.X, location.Y);
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                if (!Begin(point))
                {
                    recognizer.Enabled = false;
                    recognizer.Enabled = true;
                }
                break;
            case UIGestureRecognizerState.Changed:
                Drag(point);
                break;
            case UIGestureRecognizerState.Ended:
                _ = EndAsync(cancelled: false);
                break;
            case UIGestureRecognizerState.Cancelled or UIGestureRecognizerState.Failed:
                _ = EndAsync(cancelled: true);
                break;
        }
    }

    // A dashed frame where the item will land, under the items.
    partial void ShowPlaceholderPlatform(Rect frame, double cornerRadius)
    {
        if (_host is null)
            return;
        HidePlaceholderPlatform();
        var dark = _host.TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark;
        _placeholder = new CAShapeLayer
        {
            FillColor = UIColor.Clear.CGColor,
            StrokeColor = (dark ? UIColor.FromWhiteAlpha(1, 0.28f) : UIColor.FromWhiteAlpha(0, 0.25f)).CGColor,
            LineWidth = 1.5f,
            LineDashPattern = [5, 4],
        };
        Place(frame, cornerRadius, animated: false);
        _host.Layer.InsertSublayer(_placeholder, 0);
    }

    partial void MovePlaceholderPlatform(Rect frame) => Place(frame, null, animated: true);

    void Place(Rect frame, double? cornerRadius, bool animated)
    {
        if (_placeholder is null)
            return;
        var radius = cornerRadius is { } r ? (nfloat)r : _placeholder.CornerRadius;
        var was = _placeholder.Position;
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        _placeholder.CornerRadius = radius;
        _placeholder.Frame = new CGRect(frame.X, frame.Y, frame.Width, frame.Height);
        // Inset by half the line so the dash sits inside the item's edge.
        _placeholder.Path = UIBezierPath.FromRoundedRect(new CGRect(0.75, 0.75, frame.Width - 1.5, frame.Height - 1.5), radius).CGPath;
        CATransaction.Commit();
        // It moves with the items: the same spring from where it was.
        if (animated)
        {
            Spring(_placeholder, "position.x", (double)(was.X - _placeholder.Position.X));
            Spring(_placeholder, "position.y", (double)(was.Y - _placeholder.Position.Y));
        }
    }

    // A quick spring, damped just short of critical: the items settle in about a third of a second with a hint of
    // give, and never overshoot by more than a point or two. Additive, so a slide that starts while another is
    // running adds to it instead of cutting it off: the item keeps moving smoothly however fast the pointer goes.
    const double Stiffness = 420, Damping = 38;

    static void Spring(CALayer layer, string keyPath, double from, double to = 0)
    {
        if (Math.Abs(from - to) < 0.5 && keyPath != "transform.scale")
            return;
        var spring = new CASpringAnimation
        {
            KeyPath = keyPath,
            From = NSNumber.FromDouble(from),
            To = NSNumber.FromDouble(to),
            Additive = true,
            Mass = 1,
            Stiffness = (nfloat)Stiffness,
            Damping = (nfloat)Damping,
        };
        spring.Duration = spring.SettlingDuration;
        layer.AddAnimation(spring, null);
    }

    // The item that made room slides from where it was to its new place. Its frame has moved already; the spring
    // runs from the old place (the offset) to none.
    partial void SlidePlatform(View item, double fromX, double fromY, ref bool slid)
    {
        if (Outermost(item) is not { } platformView)
            return;
        slid = true;
        if (UIAccessibility.IsReduceMotionEnabled)
            return;
        Spring(platformView.Layer, "transform.translation.x", fromX);
        Spring(platformView.Layer, "transform.translation.y", fromY);
    }

    // The dropped item: back to its place and size with the same spring. Its translation and scale are reset at
    // once (so nothing animates them a second time) and the spring takes it from where it was shown.
    partial void SettlePlatform(View view, ref Task? settled)
    {
        if (Outermost(view) is not { } platformView)
            return;
        var (x, y, scale) = (view.TranslationX, view.TranslationY, view.Scale);
        view.TranslationX = 0;
        view.TranslationY = 0;
        view.Scale = 1;
        if (UIAccessibility.IsReduceMotionEnabled)
        {
            settled = Task.CompletedTask;
            return;
        }
        Spring(platformView.Layer, "transform.translation.x", x);
        Spring(platformView.Layer, "transform.translation.y", y);
        Spring(platformView.Layer, "transform.scale", scale - 1);
        settled = Task.Delay(TimeSpan.FromSeconds(new CASpringAnimation { Mass = 1, Stiffness = (nfloat)Stiffness, Damping = (nfloat)Damping }.SettlingDuration));
    }

    // In front of its siblings and of the placeholder layer: the subview order alone is reset when the children are
    // reordered, and a z-position keeps it on top between those changes and while it settles.
    partial void RaisePlatform(View view)
    {
        if (Outermost(view) is not { Superview: { } superview } platformView)
            return;
        superview.BringSubviewToFront(platformView);
        platformView.Layer.ZPosition = 1000;
    }

    // A view with a shadow or a clip sits in a wrapper (the handler's ContainerView); that is what lies among the
    // layout's subviews.
    static UIView? Outermost(View view) =>
        (view.Handler as IViewHandler)?.ContainerView as UIView ?? view.Handler?.PlatformView as UIView;

    partial void LowerPlatform(View view)
    {
        if (Outermost(view) is { } platformView)
            platformView.Layer.ZPosition = 0;
    }

    partial void HidePlaceholderPlatform()
    {
        _placeholder?.RemoveFromSuperLayer();
        _placeholder?.Dispose();
        _placeholder = null;
    }

    partial void ApplyActionsPlatform(View view, IReadOnlyList<ReorderAction> actions)
    {
        if (view.Handler?.PlatformView is not UIView platformView)
            return;
        platformView.AccessibilityCustomActions = actions.Count == 0
            ? null
            : [.. actions.Select(static action => new UIAccessibilityCustomAction(action.Name, (UIAccessibilityCustomActionHandler)(_ => action.Perform())))];
    }
}

#endif
