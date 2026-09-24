using AndroidX.Core.View;

namespace Plugin.Maui.Spine.Core;

internal static partial class StatusBar
{
    static partial void ApplyPlatform(StatusBarStyle style)
    {
        if (Platform.CurrentActivity?.Window is not { } window || window.DecorView is not { } decorView)
            return;

        // Light content on the bar means dark "appearance" is off.
        WindowCompat.GetInsetsController(window, decorView).AppearanceLightStatusBars = !WantsLightContent(style);
    }
}
