using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using SafeAreaEdges = Plugin.Maui.Spine.Core.SafeAreaEdges;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// Applies a page's resolved <see cref="NavigableAttribute"/> metadata to its ViewModel.
/// Shared between <see cref="NavigationService"/> and tab realization in
/// <see cref="Presentation.SpineTabbedHostPage"/>.
/// </summary>
internal static class NavigableMeta
{
    public static void Apply(View view, NavigableAttribute meta, ISystemInsetsProvider insetsProvider)
    {
        if (view.BindingContext is not ViewModelBase vm)
            return;

        PageActionDiscovery.Populate(vm);

        vm.Title = meta.Title;
        vm.TitlePlacement = meta.TitlePlacement;
        vm.TitleAlignment = meta.TitleAlignment;
        vm.IsHeaderBarVisible = meta.IsHeaderBarVisible;
        vm.IsBackButtonVisible = meta.IsBackButtonVisible;
        vm.HeaderBarMode = meta.HeaderBar;
        vm.HeaderBarForeground = meta.HeaderBarForeground is { } hex && Color.TryParse(hex, out var foreground) ? foreground : null;
        vm.StatusBarStyle = meta.StatusBarStyle;

        if (meta is NavigableTabAttribute tabMeta)
        {
            vm.IsTitleBarVisible = tabMeta.IsTitleBarVisible;
            vm.SafeAreaEdges = tabMeta.SafeAreaEdges;
            vm.ScrollInset = tabMeta.ScrollInset;
        }
        else if (meta is NavigableRegionAttribute regionMeta)
        {
            vm.IsTitleBarVisible = regionMeta.IsTitleBarVisible;
            vm.SafeAreaEdges = regionMeta.SafeAreaEdges;
            vm.ScrollInset = regionMeta.ScrollInset;
        }
        else if (meta is NavigableSheetAttribute sheetMeta)
        {
            vm.SafeAreaEdges = sheetMeta.SafeAreaEdges;
            vm.ScrollInset = sheetMeta.ScrollInset;
        }

        // Populate raw system bar dimensions and the per-page complement insets.
        var insets = insetsProvider.SystemBarInsets;
        vm.SystemBarInsets = insets;
        vm.SafeAreaInsets = Presentation.NavigationRegion.SafeAreaInsetsFor(vm, insets);

        // The page's own value on the view wins; the attribute only fills in a view that says nothing.
        if (vm.ScrollInset != SafeAreaEdges.None
            && FindFirstScrollable(view) is { } scrollable
            && !scrollable.IsSet(SafeArea.ScrollInsetProperty))
        {
            SafeArea.SetScrollInset(scrollable, vm.ScrollInset);
        }
    }

    static View? FindFirstScrollable(Element root)
    {
        var queue = new Queue<IVisualTreeElement>();
        queue.Enqueue(root);

        while (queue.TryDequeue(out var element))
        {
            foreach (var child in element.GetVisualChildren())
            {
                if (child is ScrollView or CollectionView)
                    return (View)child;

                queue.Enqueue(child);
            }
        }

        return null;
    }
}
