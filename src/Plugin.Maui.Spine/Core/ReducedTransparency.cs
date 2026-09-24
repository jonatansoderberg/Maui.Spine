namespace Plugin.Maui.Spine.Core;

/// <summary>Whether the user asked the system for fewer translucent surfaces.</summary>
internal static class ReducedTransparency
{
    /// <summary>
    /// iOS and Mac Catalyst: Reduce Transparency. Windows: transparency effects off. Android has
    /// no such setting.
    /// </summary>
    public static bool IsOn
    {
        get
        {
#if IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceTransparencyEnabled;
#elif WINDOWS
            return !new global::Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled;
#else
            return false;
#endif
        }
    }
}
