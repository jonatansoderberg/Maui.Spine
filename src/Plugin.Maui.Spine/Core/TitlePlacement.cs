namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Specifies where the page title is rendered.
/// On mobile the title typically appears in the <see cref="HeaderBar"/>;
/// on desktop it appears in the native window title bar.
/// </summary>
public enum TitlePlacement
{
    /// <summary>The title is not displayed.</summary>
    None = 0,

    /// <summary>The title is rendered inside Spine's in-page header bar. Default on mobile.</summary>
    HeaderBar = 1,

    /// <summary>The title is rendered in the native window title bar. Default on desktop.</summary>
    TitleBar = 2
}