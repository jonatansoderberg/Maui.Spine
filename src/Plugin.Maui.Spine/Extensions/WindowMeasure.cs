namespace Plugin.Maui.Spine.Extensions;

/// <summary>Measures how tall a window must be to show a page's scrolling content (see ViewModelBase.WindowHeightFor).</summary>
internal static partial class WindowMeasure
{
    public static double? HeightFor(View page, double windowWidth)
    {
        if (Services.NavigableMeta.FindFirstScrollable(page) is not ScrollView { Content: { } content } scroll
            || WindowSize(scroll) is not { Width: > 0, Height: > 0 } window || scroll.Width <= 0 || scroll.Height <= 0)
            return null;

        // What lies beside and around the scroll view stays the same at another width. Under a floating header bar the
        // scroll view fills the window and its insets keep the bar (and the Mac title bar row) clear; otherwise the
        // bar sits above it.
        var floats = page.BindingContext is Core.ViewModelBase { HeaderBarFloats: true };
        var width = windowWidth - Math.Max(0, window.Width - scroll.Width);
        var inset = SafeArea.GetResolvedInset(scroll);
        var natural = ((IView)content).Measure(width - scroll.Padding.HorizontalThickness - inset.HorizontalThickness, double.PositiveInfinity).Height;
        var outside = floats ? 0 : Math.Max(0, window.Height - scroll.Height);
        return outside + inset.Top + inset.Bottom + natural;
    }

    // The window in the points layout uses: on a Mac scaled to match iPad, MAUI's Window size is in AppKit's points.
    static Size? WindowSize(View view)
    {
#if IOS || MACCATALYST
        if (view.Handler?.PlatformView is UIKit.UIView { Window: { } window })
            return new Size((double)window.Bounds.Width, (double)window.Bounds.Height);
        return null;
#else
        return view.Window is { } window ? new Size(window.Width, window.Height) : null;
#endif
    }
}
