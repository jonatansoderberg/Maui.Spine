using Plugin.Maui.Spine.Presentation;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Where something is on screen, in the coordinates of a view shown with <see cref="SpineOverlay"/>: for a
/// package that points at a view, a header bar action or a tab, such as a tour.
/// </summary>
/// <example>
/// <code>
/// if (SpineTargets.TryGetFrame(viewModel.FilterAction, out var frame))
///     DrawHoleAround(frame);
/// </code>
/// </example>
public static partial class SpineTargets
{
    /// <summary>
    /// Finds the frame of <paramref name="target"/> in the window: a <see cref="VisualElement"/>, the view a
    /// <see cref="PageAction"/> is shown in (the header bar, or the title bar on Windows), or the item of the
    /// tab whose page is the <see cref="Type"/> given.
    /// </summary>
    /// <returns><see langword="false"/> when it is not on screen: not loaded, hidden, of no size, or not shown.</returns>
    public static bool TryGetFrame(object target, out Rect frame)
    {
        frame = default;
        return target switch
        {
            VisualElement element => TryGetElementFrame(element, out frame),
            PageAction action => PageActionView.Showing(action) is { } view && TryGetElementFrame(view, out frame),
            Type pageType => IPlatformApplication.Current?.Services.GetService<SpineHostProvider>()?.Current is SpineTabbedHostPage host
                && host.TryGetTabFrame(pageType, out frame),
            _ => false,
        };
    }

    static bool TryGetElementFrame(VisualElement element, out Rect frame)
    {
        frame = default;
        if (!element.IsLoaded || !element.IsVisible || element.Width <= 0 || element.Height <= 0
            || element.Handler?.PlatformView is not { } platformView)
        {
            return false;
        }

        return TryGetPlatformFrame(platformView, out frame);
    }

    private static partial bool TryGetPlatformFrame(object platformView, out Rect frame);
}
