#if IOS || MACCATALYST

using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Controls.Handlers.Items2;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static void ConfigureScrollInsets()
    {
        ScrollViewHandler.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);
        CollectionViewHandler.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);
        CollectionViewHandler2.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);
    }

    static void ApplyScrollInset(IElementHandler handler, IElement element)
    {
        if (element is not View view || handler.PlatformView is not UIView platformView)
            return;

        // ScrollView's platform view is the scroll view itself; a collection view's is its
        // controller's view, which UICollectionViewController makes the collection view.
        if ((platformView as UIScrollView ?? FindScrollView(platformView)) is not { } scrollView)
            return;

        var inset = SafeArea.GetResolvedInset(view);
        var edgeInsets = new UIEdgeInsets((nfloat)inset.Top, (nfloat)inset.Left, (nfloat)inset.Bottom, (nfloat)inset.Right);

        // UIKit keeps the content offset when the inset changes, so a view that was resting at
        // its top would now rest inset points too high, its first rows under the bar the inset
        // was meant to clear. Keep a resting view at its (new) top.
        var atTop = scrollView.ContentOffset.Y <= -scrollView.AdjustedContentInset.Top + 0.5;

        scrollView.ContentInset = edgeInsets;

        if (atTop)
            scrollView.ContentOffset = new CoreGraphics.CGPoint(scrollView.ContentOffset.X, -scrollView.AdjustedContentInset.Top);
        scrollView.VerticalScrollIndicatorInsets = edgeInsets;
        scrollView.HorizontalScrollIndicatorInsets = edgeInsets;
    }

    internal static UIScrollView? FindScrollView(UIView view)
    {
        foreach (var subview in view.Subviews)
        {
            if (subview is UIScrollView scrollView)
                return scrollView;
        }

        foreach (var subview in view.Subviews)
        {
            if (FindScrollView(subview) is { } nested)
                return nested;
        }

        return null;
    }
}

#endif
