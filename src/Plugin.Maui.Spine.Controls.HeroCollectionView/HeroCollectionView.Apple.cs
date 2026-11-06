#if IOS || MACCATALYST
using UIKit;

namespace Plugin.Maui.Spine.Controls;

public partial class HeroCollectionView
{
    private UIScrollView? _nativeScrollView;

    partial void OnHandlerChangedPartial() => _nativeScrollView = null;

    // How far UIKit has pulled the list past its last resting offset while bouncing off the end.
    // Without taking it off, the bounce back reads as scrolling up and expands the header.
    private double BottomOvershoot()
    {
        _nativeScrollView ??= Handler?.PlatformView is UIView view ? view as UIScrollView ?? FindScrollView(view) : null;
        if (_nativeScrollView is not { } scrollView) return 0;

        var inset = scrollView.AdjustedContentInset;
        var maxOffset = Math.Max(-inset.Top, scrollView.ContentSize.Height + inset.Bottom - scrollView.Bounds.Height);
        return Math.Max(0, scrollView.ContentOffset.Y - maxOffset);
    }

    partial void ScrollToTopPartial(bool animate)
    {
        _nativeScrollView ??= Handler?.PlatformView is UIView view ? view as UIScrollView ?? FindScrollView(view) : null;
        _nativeScrollView?.SetContentOffset(new CoreGraphics.CGPoint(0, -_nativeScrollView.AdjustedContentInset.Top), animate);
    }

    private static UIScrollView? FindScrollView(UIView view)
    {
        foreach (var subview in view.Subviews)
        {
            if ((subview as UIScrollView ?? FindScrollView(subview)) is { } scrollView)
                return scrollView;
        }

        return null;
    }

#if MACCATALYST
    // The close, minimise and zoom buttons end this far from the window's left edge, in Mac points.
    // AppKit does not tell a Catalyst app where they are.
    private const double WindowButtonsWidth = 70;

    // An app in the iPad idiom is drawn at 77 %, so the same buttons span more of its points.
    private static double WindowButtonsClearance =>
        UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Mac ? WindowButtonsWidth : WindowButtonsWidth / 0.77;

    private double _windowButtonClearance;

    // Once the header has collapsed far enough that its bottom row reaches the title bar, the row
    // slides right of the window buttons; it slides back as soon as it leaves the title bar again.
    partial void UpdateWindowButtonClearance()
    {
        if (Handler?.PlatformView is not UIView { Window: { } window } view) return;

        // Zero in full screen, where the buttons are hidden, and for a list that starts below the title bar.
        var titleBar = (double)window.SafeAreaInsets.Top - view.ConvertPointToView(CoreGraphics.CGPoint.Empty, null).Y;
        var rowTop = BottomRowTop() - (_maxHeight - _currentHeight);
        var clearance = titleBar > 0 && rowTop < titleBar ? WindowButtonsClearance : 0;

        if (clearance == _windowButtonClearance) return;
        var from = _windowButtonClearance;
        _windowButtonClearance = clearance;

        this.Animate(
            nameof(UpdateWindowButtonClearance),
            left =>
            {
                if (_headerBottomActionsLayout != null) _headerBottomActionsLayout.Margin = new Thickness(left, 0, HeaderScrollBarInset, 0);
                if (_headerOverlayLayout != null) _headerOverlayLayout.Margin = new Thickness(left, 0, 0, 0);
            },
            from, clearance, length: 200, easing: Easing.CubicOut);
    }

    // The top of the bottom row within the expanded header: the highest of the bottom content's
    // children, or the built-in title when there is no bottom content.
    private double BottomRowTop() => _headerBottomContent switch
    {
        Layout { Count: > 0 } layout => layout.Min(child => child.Frame.Y),
        { } content => content.Frame.Y,
        null => _titleLabel?.Frame.Y ?? _maxHeight,
    };
#endif
}
#endif
