using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

public partial class SpineTabbedHostPage
{
    /// <remarks>MAUI draws the tabs as a <see cref="NavigationView"/>; its items, in visual order, are the tabs.</remarks>
    private partial bool TryGetTabItemFrame(int index, out Rect frame)
    {
        frame = default;
        if (Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window { Content: { } content })
            return false;

        var items = new List<NavigationViewItem>();
        CollectTabItems(content, items);
        if (index >= items.Count)
            return false;

        frame = SpineTargets.ToWindow(items[index]);
        return true;
    }

    private static void CollectTabItems(DependencyObject parent, List<NavigationViewItem> items)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is NavigationViewItem { ActualWidth: > 0 } item)
                items.Add(item);
            else
                CollectTabItems(child, items);
        }
    }
}
