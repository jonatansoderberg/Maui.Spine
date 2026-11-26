using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static void ConfigureScrollInsets()
    {
        ScrollViewHandler.Mapper.AppendToMapping(SafeArea.MapperKey, ApplyScrollInset);
        // On the view mapper rather than a CollectionView handler's own: MAUI 11 brings CollectionViewHandler2 on
        // Windows, which Spine, built against MAUI 10, cannot name.
        ViewHandler.ViewMapper.AppendToMapping(SafeArea.MapperKey, (handler, element) =>
        {
            if (element is Microsoft.Maui.Controls.ItemsView)
                ApplyScrollInset(handler, element);
        });
    }

    static void ApplyScrollInset(IElementHandler handler, IElement element)
    {
        if (element is not View view)
            return;

        switch (handler.PlatformView)
        {
            case ScrollViewer scrollViewer:
                scrollViewer.Padding = ScrollInsetOf(view);
                break;
            case ListViewBase listView:
                listView.Padding = ScrollInsetOf(view);
                break;
            // MAUI 11's CollectionView keeps its header, rows and footer in a ScrollViewer in its template, which is
            // there once the view has loaded.
            case FrameworkElement root when FindScrollViewer(root, 0) is { } inner:
                inner.Padding = ScrollInsetOf(view);
                break;
            case FrameworkElement root when !root.IsLoaded:
                void OnLoaded(object sender, RoutedEventArgs e)
                {
                    root.Loaded -= OnLoaded;
                    if (FindScrollViewer(root, 0) is { } inner)
                        inner.Padding = ScrollInsetOf(view);
                }
                root.Loaded += OnLoaded;
                break;
        }
    }

    static Microsoft.UI.Xaml.Thickness ScrollInsetOf(View view)
    {
        var inset = SafeArea.GetResolvedInset(view);
        var margin = SafeArea.PageMarginOf(view);
        return new Microsoft.UI.Xaml.Thickness(inset.Left + margin.Left, inset.Top, inset.Right + margin.Right, inset.Bottom);
    }

    static ScrollViewer? FindScrollViewer(DependencyObject node, int depth)
    {
        if (depth > 3)
            return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if ((child as ScrollViewer ?? FindScrollViewer(child, depth + 1)) is { } found)
                return found;
        }
        return null;
    }
}
