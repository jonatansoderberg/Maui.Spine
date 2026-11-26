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

    // bringChildToFront moves a child to the end of its parent's list without detaching it, so the
    // layers' pages stay in the window (a page taken out has to be drawn again: a GL view blinks).
    partial void OrderLayersOnPlatform(View[] layers)
    {
        if (_container.Handler?.PlatformView is not Android.Views.ViewGroup group)
            return;

        foreach (var layer in layers)
        {
            if (Outer(layer) is { } view && ReferenceEquals(view.Parent, group))
                group.BringChildToFront(view);
        }

        group.Invalidate();
    }

    // The cut as the view's own clip bounds: a MAUI Clip wraps the layer in a WrapperView, which
    // takes the page in it out of the window and puts it back, as removing the clip does again.
    partial void CutBackOnPlatform(double? frontX)
    {
        if (_backLayer.Handler?.PlatformView is not Android.Views.View view)
            return;

        if (frontX is not { } x)
        {
            view.ClipBounds = null;
            return;
        }

        var density = view.Resources?.DisplayMetrics?.Density ?? 1f;
        var far = Math.Max(view.Width, view.Height) * 3;
        view.ClipBounds = new Android.Graphics.Rect(-far, -far, (int)Math.Round(Math.Max(0, x) * density), far);
    }

    private static Android.Views.View? Outer(View view) =>
        view.Handler is IPlatformViewHandler handler ? handler.ContainerView ?? handler.PlatformView : null;
}
