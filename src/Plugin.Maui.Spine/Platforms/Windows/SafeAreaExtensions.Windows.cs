using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static partial void ConfigureHandlers(MauiAppBuilder builder)
    {
        ScrollViewHandler.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);
        CollectionViewHandler.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);
    }

    static void ApplyScrollInset(IElementHandler handler, IElement element)
    {
        if (element is not View view)
            return;

        var inset = SafeArea.GetResolvedInset(view);
        var thickness = new Microsoft.UI.Xaml.Thickness(inset.Left, inset.Top, inset.Right, inset.Bottom);

        switch (handler.PlatformView)
        {
            case ScrollViewer scrollViewer:
                scrollViewer.Padding = thickness;
                break;
            case ListViewBase listView:
                listView.Padding = thickness;
                break;
        }
    }
}
