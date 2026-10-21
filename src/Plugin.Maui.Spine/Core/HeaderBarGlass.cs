namespace Plugin.Maui.Spine.Core;

/// <summary>The Liquid Glass of the header bar's buttons on iOS 26, where the app has glass header actions on.</summary>
public enum HeaderBarGlass
{
    /// <summary>Frosted glass, as a navigation bar's buttons have.</summary>
    Regular,

    /// <summary>Nearly transparent glass, for a bar that lies over a photo or a map.</summary>
    Clear,

    /// <summary>
    /// No glass at rest; regular glass while a button is pressed, like a toolbar's icons. A prominent action keeps
    /// its tinted glass.
    /// </summary>
    Transient
}
