using Plugin.Maui.Spine.Core;
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

        vm.Title = meta.Title;
        vm.TitlePlacement = meta.TitlePlacement;
        vm.TitleAlignment = meta.TitleAlignment;
        vm.IsHeaderBarVisible = meta.IsHeaderBarVisible;
        vm.IsBackButtonVisible = meta.IsBackButtonVisible;

        if (meta is NavigableTabAttribute tabMeta)
        {
            vm.IsTitleBarVisible = tabMeta.IsTitleBarVisible;
            vm.SafeAreaEdges = tabMeta.SafeAreaEdges;
        }
        else if (meta is NavigableRegionAttribute regionMeta)
        {
            vm.IsTitleBarVisible = regionMeta.IsTitleBarVisible;
            vm.SafeAreaEdges = regionMeta.SafeAreaEdges;
        }
        else if (meta is NavigableSheetAttribute sheetMeta)
        {
            vm.SafeAreaEdges = sheetMeta.SafeAreaEdges;
        }

        // Populate raw system bar dimensions and the per-page complement insets.
        var insets = insetsProvider.SystemBarInsets;
        vm.SystemBarInsets = insets;
        vm.SafeAreaInsets = new Thickness(
            (vm.SafeAreaEdges & SafeAreaEdges.Left)   != 0 ? 0 : insets.Left,
            (vm.SafeAreaEdges & SafeAreaEdges.Top)    != 0 ? 0 : insets.Top,
            (vm.SafeAreaEdges & SafeAreaEdges.Right)  != 0 ? 0 : insets.Right,
            (vm.SafeAreaEdges & SafeAreaEdges.Bottom) != 0 ? 0 : insets.Bottom);
    }
}
