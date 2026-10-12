using Microsoft.UI.Xaml;

namespace Plugin.Maui.Spine.Core;

public static partial class SpineTargets
{
    private static partial bool TryGetPlatformFrame(object platformView, out Rect frame)
    {
        frame = default;
        if (platformView is not FrameworkElement { IsLoaded: true, XamlRoot: not null } element)
            return false;

        frame = ToWindow(element);
        return true;
    }

    // Relative to the XamlRoot, which a popup (a sheet, an overlay) shares with the window.
    internal static Rect ToWindow(FrameworkElement element)
    {
        var at = element.TransformToVisual(null).TransformPoint(new global::Windows.Foundation.Point(0, 0));
        return new Rect(at.X, at.Y, element.ActualWidth, element.ActualHeight);
    }
}
