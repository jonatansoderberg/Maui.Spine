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

    private static UIScrollView? FindScrollView(UIView view)
    {
        foreach (var subview in view.Subviews)
        {
            if ((subview as UIScrollView ?? FindScrollView(subview)) is { } scrollView)
                return scrollView;
        }

        return null;
    }
}
#endif
