#if !ANDROID

#if IOS || MACCATALYST
using UIKit;
#endif

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Default <see cref="ISystemInsetsProvider"/> for platforms that do not require
/// manual system bar inset management (Windows, macOS).
/// iOS and Mac Catalyst report real <c>UIWindow.safeAreaInsets</c> so that
/// <see cref="Presentation.NavigationRegion"/> can apply them explicitly instead
/// of relying on MAUI's automatic ISafeAreaView2 geometry.
/// </summary>
internal sealed class SystemInsetsProvider : ISystemInsetsProvider
{
#if IOS
    private Thickness _systemBarInsets;

    /// <inheritdoc/>
    public Thickness SystemBarInsets => _systemBarInsets;

    /// <inheritdoc/>
    public event Action? InsetsChanged;

    /// <summary>
    /// Reads the current safe-area insets from the given <paramref name="uiWindow"/> and fires
    /// <see cref="InsetsChanged"/> if the values have changed.
    /// UIKit points are already device-independent units — no density conversion needed.
    /// </summary>
    internal void UpdateFromUIWindow(UIWindow uiWindow)
    {
        var si = uiWindow.SafeAreaInsets;
        var newInsets = new Thickness(
            (double)si.Left,
            (double)si.Top,
            (double)si.Right,
            (double)si.Bottom);

        if (newInsets == _systemBarInsets) return;
        _systemBarInsets = newInsets;
        InsetsChanged?.Invoke();
    }

    /// <summary>
    /// Reads insets from the key UIWindow discovered via <c>ConnectedScenes</c>.
    /// Used by the iOS hook where the MAUI handler's platform view may not be available.
    /// </summary>
    internal void UpdateFromUIWindow()
    {
        var window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow)
            ?? UIApplication.SharedApplication.ConnectedScenes
                .OfType<UIWindowScene>()
                .SelectMany(s => s.Windows)
                .FirstOrDefault();

        if (window is null) return;
        UpdateFromUIWindow(window);
    }
#elif MACCATALYST
    // SystemBarInsets stays zero on Mac Catalyst. After fullSizeContentView is enabled,
    // MAUI's own safeAreaInsets mechanism pushes NavigationRegion down by the title bar
    // height. NavigationRegion counteracts that via a per-page negative container margin
    // (full-bleed pages only), using MacTitleBarHeight below rather than SystemBarInsets,
    // so that ApplySafeAreaPadding on non-full-bleed pages is unaffected.
    public Thickness SystemBarInsets => Thickness.Zero;

    public event Action? InsetsChanged;

    private double _macTitleBarHeight;

    internal double MacTitleBarHeight => _macTitleBarHeight;

    /// <summary>
    /// Stores the measured native title-bar height and fires <see cref="InsetsChanged"/>
    /// so <see cref="Presentation.NavigationRegion"/> can update its container margin.
    /// </summary>
    internal void SetMacTitleBarHeight(double height)
    {
        if (Math.Abs(_macTitleBarHeight - height) < 0.5) return;
        _macTitleBarHeight = height;
        InsetsChanged?.Invoke();
    }

    internal void UpdateFromUIWindow(UIWindow uiWindow)
    {
        var top = (double)uiWindow.SafeAreaInsets.Top;
        if (top == 0) top = 28; // standard Mac title bar fallback
        SetMacTitleBarHeight(top);
    }
#else
    /// <inheritdoc/>
    public Thickness SystemBarInsets => Thickness.Zero;

    /// <inheritdoc/>
    public event Action? InsetsChanged { add { } remove { } }
#endif
}

#endif
