#if IOS || MACCATALYST
using Plugin.Maui.Spine.Core;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

public sealed partial class NavigationRegion
{
    partial void RestrictBackSwipeOnPlatform()
    {
        _contentHostFront.HandlerChanged += (_, _) => RestrictBackSwipe();
        _contentHostFront.Loaded += (_, _) => RestrictBackSwipe();
    }

#if IOS
    // The focused field may be anywhere under the region, including in a sheet presented over it
    // (a view controller of its own, so not under this view): the sheet's own region takes that one.
    private bool ContainsFocus() =>
        _contentHostFront.Handler?.PlatformView is UIView view && FindFirstResponder(view) is not null;

    private static UIView? FindFirstResponder(UIView view)
    {
        if (view.IsFirstResponder)
            return view;

        foreach (var subview in view.Subviews)
        {
            if (FindFirstResponder(subview) is { } found)
                return found;
        }

        return null;
    }

    private double VisibleBottomOnScreen()
    {
        if (_contentHostFront.Handler?.PlatformView is not UIView view || view.Window?.Screen is not { } screen)
            return 0;

        return view.ConvertRectToCoordinateSpace(view.Bounds, screen.CoordinateSpace).Bottom;
    }

    /// <summary>
    /// Lays the region out again inside a UIKit animation with the keyboard's own duration and
    /// curve, so the content's new frames move with the keyboard rather than jump ahead of it.
    /// </summary>
    private void AnimateWithKeyboard(Action apply)
    {
        if (SoftKeyboard.Duration <= TimeSpan.Zero || Handler?.PlatformView is not UIView { Window: { } window })
        {
            apply();
            return;
        }

        var options = (UIViewAnimationOptions)((ulong)SoftKeyboard.Curve << 16)
            | UIViewAnimationOptions.BeginFromCurrentState
            | UIViewAnimationOptions.AllowUserInteraction;

        UIView.Animate(SoftKeyboard.Duration.TotalSeconds, 0, options, () =>
        {
            apply();
            window.LayoutIfNeeded();
        }, () => { });
    }
#endif

    /// <summary>
    /// Lays the region out for the search's new state inside one UIKit animation, so the bar's
    /// buttons, the title, the field and the list's inset all move on the same curve; under Reduce
    /// Motion the region cross-fades to it instead.
    /// </summary>
    private void AnimateSearchOnPlatform(Action apply, Action completed)
    {
        if (Handler?.PlatformView is not UIView { Window: { } window } view)
        {
            apply();
            completed();
            return;
        }

        void Run()
        {
            apply();
            window.LayoutIfNeeded();
        }

        var options = UIViewAnimationOptions.BeginFromCurrentState | UIViewAnimationOptions.AllowUserInteraction;

        if (ReducedMotion.IsOn)
            UIView.Transition(view, SearchDuration / 1000.0 * 2 / 3, options | UIViewAnimationOptions.TransitionCrossDissolve, Run, completed);
        else
            UIView.AnimateNotify(SearchDuration / 1000.0, 0, 1, 0, options, Run, _ => completed());
    }

    private bool? _frontClippedBeforeRound;

    partial void RoundFront(bool round)
    {
#if IOS
        if (!OperatingSystem.IsIOSVersionAtLeast(26) || _contentHostFront.Handler?.PlatformView is not UIView view)
            return;

        if (ViewModel.Presentation is NavigationPresentation.Sheet)
        {
            // The layer reaches above the sheet, where the status bar would be, so its own corners
            // fall outside it: the visible part is masked to the sheet's rounded shape instead.
            view.MaskView = round && SheetShape(view) is { } shape
                ? new UIView(shape.Visible) { BackgroundColor = UIColor.Black, Layer = { CornerRadius = shape.Radius, CornerCurve = CoreAnimation.CACornerCurve.Continuous } }
                : null;
            return;
        }

        if (round)
        {
            // Concentric with the window, which is the screen's own corner radius; UIKit has no
            // public property for that radius itself.
            _frontClippedBeforeRound ??= view.ClipsToBounds;
            view.CornerConfiguration = UICornerConfiguration.CreateUniformCorners(UICornerRadius.CreateContainerConcentric());
            view.ClipsToBounds = true;
        }
        else if (_frontClippedBeforeRound is { } clipped)
        {
            view.CornerConfiguration = UICornerConfiguration.CreateUniformCorners(UICornerRadius.CreateFixed(0));
            view.ClipsToBounds = clipped;
            _frontClippedBeforeRound = null;
        }
#endif
    }

#if IOS
    /// <summary>
    /// The part of <paramref name="view"/> inside the sheet, in its own coordinates, and the
    /// sheet's corner radius: the sheet's view is the first one up that UIKit rounds.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("ios26.0")]
    private static (CoreGraphics.CGRect Visible, nfloat Radius)? SheetShape(UIView view)
    {
        for (var ancestor = view.Superview; ancestor is not null; ancestor = ancestor.Superview)
        {
            // effectiveRadiusForCorner:, a getter despite the binding's name.
            var radius = ancestor.SetEffectiveRadius(UIRectCorner.TopLeft);
            if (radius > 0)
                return (CoreGraphics.CGRect.Intersect(view.Bounds, view.ConvertRectFromView(ancestor.Bounds, ancestor)), radius);
        }

        return null;
    }
#endif

    partial void CutBackOnPlatform(double? frontX)
    {
        if (_backLayer.Handler?.PlatformView is not UIView layer)
            return;

        if (frontX is not { } x)
        {
            layer.MaskView = null;
            return;
        }

        var bounds = layer.Bounds;
        var frame = new CoreGraphics.CGRect(-bounds.Width, -bounds.Height, bounds.Width + (nfloat)Math.Max(0, x), bounds.Height * 3);

        // Set inside the animation that moves the front page, the frame change animates with it.
        if (layer.MaskView is { } mask)
            mask.Frame = frame;
        else
            layer.MaskView = new UIView(frame) { BackgroundColor = UIColor.Black };
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
