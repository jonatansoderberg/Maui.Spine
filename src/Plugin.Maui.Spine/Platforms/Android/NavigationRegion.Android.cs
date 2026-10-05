using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

public sealed partial class NavigationRegion
{
    // A sheet is a dialog window of its own; the keyboard for its field is still reported by the
    // activity's root, so only the region that holds the focus takes it.
    private bool ContainsFocus() =>
        _contentHostFront.Handler?.PlatformView is Android.Views.ViewGroup view && view.FindFocus() is not null;

    /// <summary>
    /// Where the visible part of the region ends on screen. A sheet always reaches the bottom of the
    /// screen: Material keeps it full height and slides it, and expands it while the keyboard comes
    /// up, so its own frame says nothing until that settles. A region ends where its view does.
    /// </summary>
    private double VisibleBottomOnScreen()
    {
        if (_contentHostFront.Handler?.PlatformView is not Android.Views.View view)
            return 0;

        var measured = ViewModel.Presentation is NavigationPresentation.Sheet ? view.RootView ?? view : view;
        var density = (double)(view.Resources?.DisplayMetrics?.Density ?? 1f);
        var location = new int[2];
        measured.GetLocationOnScreen(location);
        return (location[1] + measured.Height) / density;
    }
}
