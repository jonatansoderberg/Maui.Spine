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

        // Setting the attribute's values below is not a change made by the page.
        vm.ReapplyHeaderBar = null;

        vm.Title = meta.Title;
        vm.TitlePlacement = meta.TitlePlacement;
        vm.TitleAlignment = meta.TitleAlignment;
        vm.IsHeaderBarVisible = meta.IsHeaderBarVisible;
        vm.IsBackButtonVisible = meta.IsBackButtonVisible;
        vm.HeaderBarMode = meta.HeaderBar;
        vm.LargeTitle = meta.LargeTitle;
        vm.HeaderBarBackground = meta.HeaderBarBackground;
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

        vm.EffectiveHeaderBarBackground = ResolveBackground(view, vm, meta);

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

        if (vm.HeaderBarFloats)
            HeaderBar.Track(view, vm);

        vm.ReapplyHeaderBar = () => ReapplyHeaderBar(view, vm, meta);
    }

    /// <summary>
    /// Resolves the header again when the page changes <see cref="ViewModelBase.HeaderBarBackground"/>,
    /// <see cref="ViewModelBase.HeaderBarMode"/> or <see cref="ViewModelBase.LargeTitle"/> while it is
    /// shown: the page may now float under the bar, or no longer, which changes the insets its
    /// content keeps clear.
    /// </summary>
    static void ReapplyHeaderBar(View view, ViewModelBase vm, NavigableAttribute meta)
    {
        vm.EffectiveHeaderBarBackground = ResolveBackground(view, vm, meta);
        vm.SafeAreaInsets = Presentation.NavigationRegion.SafeAreaInsetsFor(vm, vm.SystemBarInsets);

        if (vm.HeaderBarFloats)
            HeaderBar.Track(view, vm);

        HeaderBar.UpdateScrollInset(view);
    }

    /// <summary>
    /// Resolves <see cref="HeaderBarBackground.Auto"/> and falls back from the scroll edge values
    /// where they cannot be drawn as asked.
    /// </summary>
    static HeaderBarBackground ResolveBackground(View view, ViewModelBase vm, NavigableAttribute meta)
    {
        var background = vm.HeaderBarBackground;

        if (background == HeaderBarBackground.Auto)
        {
            // The hard style, as behind a navigation bar. Only where the system draws the effect, and only for a page whose scroll
            // view fills it from the top: anything above the list that does not scroll would
            // otherwise sit under the bar for good.
            background = vm.HeaderBarMode == HeaderBarMode.Overlay
                ? HeaderBarBackground.Transparent
                : HasSystemScrollEdge
                    && meta.Presentation is not NavigationPresentation.Sheet
                    && vm.IsHeaderBarVisible
                    && (HeaderBar.GetScrollSource(view) ?? FindFirstScrollable(view)) is { } source
                    && FillsFromTop(view, source)
                        ? HeaderBarBackground.ScrollEdgeHard
                        : HeaderBarBackground.Solid;
        }

        if (background.IsScrollEdge() && HasSystemScrollEdge is false && IsApple)
            background = HeaderBarBackground.Solid;

        if (background.IsScrollEdge() && ReducedTransparency.IsOn)
            background = HeaderBarBackground.Solid;

        // With no bar there is nothing for content to be behind.
        if (!vm.IsHeaderBarVisible)
            background = HeaderBarBackground.Solid;

        return background;
    }

    static bool IsApple => OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();

    /// <summary>Whether UIKit draws the scroll edge effect: iOS and Mac Catalyst 26.</summary>
    internal static bool HasSystemScrollEdge =>
        OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26);

    /// <summary>
    /// Whether <paramref name="source"/> covers <paramref name="page"/> from its top edge: every
    /// container between them holds only that branch, or is a grid in which that branch spans all
    /// rows (a list with a floating button over it).
    /// </summary>
    static bool FillsFromTop(Element page, Element source)
    {
        for (var child = source; child.Parent is { } parent && !ReferenceEquals(child, page); child = child.Parent)
        {
            if (ReferenceEquals(parent, page) || parent is ContentView or Border or ScrollView)
                continue;

            if (parent is Grid grid && child is BindableObject cell
                && Grid.GetRow(cell) == 0
                && Grid.GetRowSpan(cell) >= Math.Max(1, grid.RowDefinitions.Count))
                continue;

            if (parent is Layout layout && layout.Count == 1)
                continue;

            return false;
        }

        return true;
    }

    internal static View? FindFirstScrollable(Element root)
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
