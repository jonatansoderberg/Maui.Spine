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
        // How far a page shorter than the screen scrolls depends on how tall its content is.
        ScrollViewHandler.Mapper.AppendToMapping(nameof(IScrollView.ContentSize), ApplyScrollInset);
        CollectionViewHandler.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);
        CollectionViewHandler2.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);

        // A new items layout is a new UICollectionViewLayout, without the page margin.
        CollectionViewHandler2.Mapper.AppendToMapping(nameof(StructuredItemsView.ItemsLayout), ApplyScrollInset);

        CollectionViewHandler.Mapper.PrependToMapping(nameof(ItemsView.ItemsSource), NoteOffsetBeforeItemsSource);
        CollectionViewHandler.Mapper.AppendToMapping(nameof(ItemsView.ItemsSource), ScrollToTopInsideInset);
        CollectionViewHandler2.Mapper.PrependToMapping(nameof(ItemsView.ItemsSource), NoteOffsetBeforeItemsSource);
        CollectionViewHandler2.Mapper.AppendToMapping(nameof(ItemsView.ItemsSource), ScrollToTopInsideInset);
    }

    static CoreGraphics.CGPoint? _offsetBeforeItemsSource;

    static void NoteOffsetBeforeItemsSource(IElementHandler handler, IElement element) =>
        _offsetBeforeItemsSource = ScrollViewOf(handler)?.ContentOffset;

    /// <summary>
    /// MAUI 11 takes a collection view back to its top when its ItemsSource changes by setting the
    /// offset to zero, which is the top only without a content inset: under the header bar's inset
    /// the first rows were left under the bar (a search that filters the list, then ends). The top
    /// it means is minus the inset. MAUI 10 leaves the offset alone, and so does this then.
    /// </summary>
    static void ScrollToTopInsideInset(IElementHandler handler, IElement element)
    {
        var before = _offsetBeforeItemsSource;
        _offsetBeforeItemsSource = null;

        if (ScrollViewOf(handler) is not { } scrollView)
            return;

        var inset = scrollView.AdjustedContentInset;

        if (before is { } offset && offset != CoreGraphics.CGPoint.Empty && scrollView.ContentOffset == CoreGraphics.CGPoint.Empty
            && (inset.Top != 0 || inset.Left != 0))
            scrollView.ContentOffset = new CoreGraphics.CGPoint(-inset.Left, -inset.Top);
    }

    static UIScrollView? ScrollViewOf(IElementHandler handler) =>
        handler.PlatformView is UIView view ? view as UIScrollView ?? FindScrollView(view) : null;

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
        var contentInsets = edgeInsets;

        // MAUI arranges a ScrollView's content at least as tall as the ScrollView, so a page shorter
        // than the screen would still scroll by the top and bottom insets, under the header bar and
        // into empty space under its last row. It scrolls only as far as its content needs to come
        // clear of the bars, and not at all when it fits: the bottom inset may go below zero, down to
        // minus the top one, which takes back the range the top inset adds.
        if (inset.Top + inset.Bottom > 0 && view is ScrollView { Orientation: not ScrollOrientation.Horizontal, Content: IView content } scrolling)
        {
            var used = content.DesiredSize.Height + scrolling.Padding.VerticalThickness;
            var arranged = Math.Max(used, scrollView.Frame.Height);
            contentInsets.Bottom = (nfloat)Math.Max(-inset.Top, used + inset.Bottom - arranged);
        }

        // Content that grows or shrinks maps here again. An unchanged inset leaves the scroll view
        // alone: setting it again, or moving the offset, undoes a running pull-to-refresh's offset
        // and leaves the page scrolled under the header bar once the refresh ends.
        if (scrollView.ContentInset != contentInsets)
        {
            // UIKit keeps the content offset when the inset changes, so a view that was resting at
            // its top would now rest inset points too high, its first rows under the bar the inset
            // was meant to clear. Keep a resting view at its (new) top.
            var atTop = scrollView.ContentInset.Top != contentInsets.Top &&
                scrollView.ContentOffset.Y <= -scrollView.AdjustedContentInset.Top + 0.5;

            scrollView.ContentInset = contentInsets;

            if (atTop)
                scrollView.ContentOffset = new CoreGraphics.CGPoint(scrollView.ContentOffset.X, -scrollView.AdjustedContentInset.Top);
        }

        if (scrollView.VerticalScrollIndicatorInsets != edgeInsets)
        {
            scrollView.VerticalScrollIndicatorInsets = edgeInsets;
            scrollView.HorizontalScrollIndicatorInsets = edgeInsets;
        }

        if (scrollView is UICollectionView collectionView)
            // A list's rows carry the margin themselves (RowMargins); only a grid's sections are inset.
            ApplyPageMargin(collectionView, RowMargins.PerRow(view) ? Thickness.Zero : SafeArea.PageMarginOf(view));

#if MACCATALYST
        if (view is ScrollView scroll)
        {
            ScrollFit.Attach(scroll);
            ScrollFit.Update(scroll);
        }
#endif
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
