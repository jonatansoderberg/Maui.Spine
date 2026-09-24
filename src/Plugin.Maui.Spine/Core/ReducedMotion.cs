namespace Plugin.Maui.Spine.Core;

/// <summary>Whether the user asked the system for less motion.</summary>
internal static class ReducedMotion
{
    static bool _isOn;
    static bool _read;
    static long _readAt;

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
            if (!_read || now - _readAt > 1000)
            {
                _isOn = Read();
                _read = true;
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
        // "Remove animations" sets the animator duration scale to 0.
        return Android.Provider.Settings.Global.GetFloat(
            Android.App.Application.Context.ContentResolver,
            Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
#elif WINDOWS
        return !new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
        return false;
#endif
    }
}
