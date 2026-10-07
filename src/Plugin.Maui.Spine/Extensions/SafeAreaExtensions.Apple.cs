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

        // A new items layout is a new UICollectionViewLayout, without the page margin.
        CollectionViewHandler2.Mapper.AppendToMapping(nameof(StructuredItemsView.ItemsLayout), ApplyScrollInset);
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

        if (scrollView is UICollectionView collectionView)
            ApplyPageMargin(collectionView, SafeArea.PageMarginOf(view));
    }

    /// <summary>
    /// Insets a collection view's sections by <paramref name="margin"/> on the left and right. A
    /// compositional layout lays its sections out inside the insets its configuration refers to,
    /// and ignores the scroll view's own side content inset; so the margin becomes the collection
    /// view's layout margins, and the layout refers to those.
    /// </summary>
    static void ApplyPageMargin(UICollectionView collectionView, Thickness margin)
    {
        if (collectionView.CollectionViewLayout is not UICollectionViewCompositionalLayout layout)
            return;

        var reference = margin == Thickness.Zero ? UIContentInsetsReference.Automatic : UIContentInsetsReference.LayoutMargins;
        var margins = new NSDirectionalEdgeInsets(0, (nfloat)margin.Left, 0, (nfloat)margin.Right);
        var configuration = layout.Configuration;

        if (configuration.ContentInsetsReference == reference && collectionView.DirectionalLayoutMargins == margins
            && configuration.BoundarySupplementaryItems.All(item => item.ContentInsets == margins))
            return;

        // Only the margins asked for: not the superview's, and not the safe area, which Spine's
        // own scroll inset already keeps clear (the top one would push the rows down twice).
        collectionView.PreservesSuperviewLayoutMargins = false;
        collectionView.InsetsLayoutMarginsFromSafeArea = false;
        collectionView.DirectionalLayoutMargins = margins;

        // A list's header and footer belong to the whole layout rather than to a section, and the
        // insets the sections refer to do not reach them; a grid's are its section's. Both end up
        // inside the margin.
        foreach (var item in configuration.BoundarySupplementaryItems)
            item.ContentInsets = margins;

        // The configuration is handed out as a copy, and the layout only takes a whole new one.
        configuration.ContentInsetsReference = reference;
        layout.Configuration = configuration;
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
