#if WINDOWS
using Microsoft.UI.Windowing;

namespace Plugin.Maui.Spine.Biometrics;

internal sealed partial class AppLockService
{
    /// <remarks>A window hidden to the tray (Spine's <c>CloseToBackground</c>) counts as minimised.</remarks>
    partial void WatchMinimised(Window window)
    {
        if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window { AppWindow: { } appWindow })
            return;

        var away = IsAway(appWindow);
        appWindow.Changed += (sender, _) =>
        {
            if (IsAway(sender) == away)
                return;

            away = !away;
            if (away)
                OnStopped(null, EventArgs.Empty);
            else
                OnResumed(null, EventArgs.Empty);
        };
    }

    private static bool IsAway(AppWindow window) =>
        !window.IsVisible || window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
}
#endif
