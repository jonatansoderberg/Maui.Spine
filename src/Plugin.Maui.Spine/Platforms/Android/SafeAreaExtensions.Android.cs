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
    }

    static void ApplyScrollInset(IElementHandler handler, IElement element)
    {
        if (element is not Microsoft.Maui.Controls.View view || handler.PlatformView is not ViewGroup platformView || platformView.Context is not { } context)
            return;

        var inset = SafeArea.GetResolvedInset(view);

        // Padding with clipToPadding off is Android's content inset: the scrollable range grows
        // by the padding while children still draw through it.
        platformView.SetClipToPadding(false);
        platformView.SetPadding(
            (int)context.ToPixels(inset.Left),
            (int)context.ToPixels(inset.Top),
            (int)context.ToPixels(inset.Right),
            (int)context.ToPixels(inset.Bottom));
    }
}
