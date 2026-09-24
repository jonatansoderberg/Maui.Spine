namespace Plugin.Maui.Spine.Core;

/// <summary>Whether the user asked the system for less motion.</summary>
internal static class ReducedMotion
{
    static bool _isOn;
    static long _readAt = long.MinValue;

    /// <summary>
    /// iOS and Mac Catalyst: Reduce Motion. Android: animations removed (animator duration scale
    /// 0). Windows: animation effects off. Read at most once a second, because scroll handlers ask
    /// on every frame and the setting can change while the app runs.
    /// </summary>
    public static bool IsOn
    {
        get
        {
            var now = Environment.TickCount64;
            if (now - _readAt > 1000)
            {
                _isOn = Read();
                _readAt = now;
            }

            return _isOn;
        }
    }

    static bool Read()
    {
#if IOS || MACCATALYST
        return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
        return OperatingSystem.IsAndroidVersionAtLeast(26)
            ? !Android.Animation.ValueAnimator.AreAnimatorsEnabled()
            : Android.Provider.Settings.Global.GetFloat(
                  Android.App.Application.Context.ContentResolver,
                  Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
#elif WINDOWS
        return !new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
        return false;
#endif
    }
}
