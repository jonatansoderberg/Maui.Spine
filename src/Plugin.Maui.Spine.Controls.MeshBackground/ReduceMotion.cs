#if ANDROID
using Android.Provider;
#endif

namespace Plugin.Maui.Spine.Controls;

/// <summary>The system's Reduce Motion setting (Remove animations on Android, Animation effects off on Windows).</summary>
internal static class ReduceMotion
{
    public static bool IsEnabled
    {
        get
        {
#if IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            var resolver = Android.App.Application.Context.ContentResolver;
            return resolver is not null && Settings.Global.GetFloat(resolver, Settings.Global.AnimatorDurationScale, 1f) == 0f;
#elif WINDOWS
            return !new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
            return false;
#endif
        }
    }
}
