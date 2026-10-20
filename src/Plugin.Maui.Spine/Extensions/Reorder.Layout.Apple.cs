#if IOS || MACCATALYST

using CoreAnimation;
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
        CATransaction.Begin();
        CATransaction.DisableActions = !animated;
        CATransaction.AnimationDuration = SlideMs / 1000.0;
        _placeholder.CornerRadius = radius;
        _placeholder.Frame = new CGRect(frame.X, frame.Y, frame.Width, frame.Height);
        // Inset by half the line so the dash sits inside the item's edge.
        _placeholder.Path = UIBezierPath.FromRoundedRect(new CGRect(0.75, 0.75, frame.Width - 1.5, frame.Height - 1.5), radius).CGPath;
        CATransaction.Commit();
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
