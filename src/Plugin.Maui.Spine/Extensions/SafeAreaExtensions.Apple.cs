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

        scrollView.ContentInset = edgeInsets;
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
