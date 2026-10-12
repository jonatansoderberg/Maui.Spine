namespace Plugin.Maui.Spine.Core;

public static partial class SpineTargets
{
    // On screen, less the activity's own place: a view in a sheet lives in the sheet's window, and its
    // place in that window would be off by the sheet's.
    private static partial bool TryGetPlatformFrame(object platformView, out Rect frame)
    {
        frame = default;
        if (platformView is not Android.Views.View { IsAttachedToWindow: true } view)
            return false;

        frame = ToWindow(view, 0, 0, view.Width, view.Height);
        return true;
    }

    /// <summary>A rectangle of <paramref name="view"/>, in pixels, as a frame in the activity's window, in units.</summary>
    internal static Rect ToWindow(Android.Views.View view, int x, int y, int width, int height)
    {
        var density = view.Resources?.DisplayMetrics?.Density ?? 1;
        var at = new int[2];
        view.GetLocationOnScreen(at);

        var origin = new int[2];
        Platform.CurrentActivity?.Window?.DecorView?.GetLocationOnScreen(origin);

        return new Rect((at[0] - origin[0] + x) / density, (at[1] - origin[1] + y) / density, width / density, height / density);
    }
}
