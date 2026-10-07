using AndroidX.Core.View;

namespace Plugin.Maui.Spine.Core;

internal static partial class StatusBar
{
    static partial void ApplyPlatform(StatusBarStyle style)
    {
        if (Platform.CurrentActivity?.Window is not { } window || window.DecorView is not { } decorView)
            return;

        // Light content on the bar means dark "appearance" is off.
        WindowCompat.GetInsetsController(window, decorView)?.AppearanceLightStatusBars = !WantsLightContent(style);
    }

    static partial void SetHiddenPlatform(bool hidden)
    {
        // A lightbox over a sheet is a window of its own, whose status bar is the one showing.
        if ((Plugin.Maui.Spine.Presentation.LightboxOverlay.CurrentWindow ?? Platform.CurrentActivity?.Window) is not { } window || window.DecorView is not { } decorView
            || WindowCompat.GetInsetsController(window, decorView) is not { } controller)
            return;

        // The bar comes back with a swipe from the edge while hidden, as it does over a full-screen photo.
        controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        if (hidden)
            controller.Hide(WindowInsetsCompat.Type.StatusBars());
        else
            controller.Show(WindowInsetsCompat.Type.StatusBars());
    }
}
