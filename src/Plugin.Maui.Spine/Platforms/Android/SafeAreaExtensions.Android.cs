using Android.Views;
using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static void ConfigureScrollInsets()
    {
        ScrollViewHandler.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);
        CollectionViewHandler.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);

        // MAUI sets the list's padding again when its items layout changes.
        CollectionViewHandler.Mapper.AppendToMapping(nameof(StructuredItemsView.ItemsLayout), ApplyScrollInset);
    }

    static void ApplyScrollInset(IElementHandler handler, IElement element)
    {
        if (element is not Microsoft.Maui.Controls.View view || handler.PlatformView is not ViewGroup platformView || platformView.Context is not { } context)
            return;

        var inset = SafeArea.GetResolvedInset(view);
        var margin = SafeArea.PageMarginOf(view);

        // MAUI pads a list with item spacing by minus that spacing, so the spacing each item gets
        // on every side does not show around the list's edges; that padding is kept under ours.
        var (spacingX, spacingY) = (0, 0);
        if (platformView is AndroidX.RecyclerView.Widget.RecyclerView list)
        {
            for (var i = 0; i < list.ItemDecorationCount; i++)
            {
                if (list.GetItemDecorationAt(i) is SpacingItemDecoration decoration)
                    (spacingX, spacingY) = (decoration.HorizontalOffset, decoration.VerticalOffset);
            }
        }

        // Padding with clipToPadding off is Android's content inset: the scrollable range grows
        // by the padding while children still draw through it.
        platformView.SetClipToPadding(false);
        platformView.SetPadding(
            (int)context.ToPixels(inset.Left + margin.Left) - spacingX,
            (int)context.ToPixels(inset.Top) - spacingY,
            (int)context.ToPixels(inset.Right + margin.Right) - spacingX,
            (int)context.ToPixels(inset.Bottom) - spacingY);
    }
}
