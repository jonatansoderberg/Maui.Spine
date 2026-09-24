namespace Plugin.Maui.Spine.Core;

/// <summary>How Spine's header bar sits on a page.</summary>
public enum HeaderBarMode
{
    /// <summary>The header bar takes its own row above the content.</summary>
    Normal,

    /// <summary>
    /// The header bar floats over the content, which starts at the top of the screen behind the
    /// status bar and the bar: for pages that open on a photo, a map or a hero. The title and the
    /// actions keep a fixed colour through <c>HeaderBarForeground</c>, and
    /// <see cref="ViewModelBase.SafeAreaInsets"/> reports the height the content must keep clear
    /// (status bar plus header bar) so a list can take it with <c>SafeArea.ScrollInset="Top"</c>.
    /// </summary>
    Overlay,
}

/// <summary>
/// What is behind the header bar's title and actions while content scrolls under it: under an
/// <see cref="HeaderBarMode.Overlay"/> header, and on a page with a large title (whose content
/// always scrolls under the bar).
/// </summary>
public enum HeaderBarBackground
{
    /// <summary>
    /// <see cref="Clear"/> under an <see cref="HeaderBarMode.Overlay"/> header, whose page draws its
    /// own top; <see cref="Solid"/> otherwise.
    /// </summary>
    Auto,

    /// <summary>
    /// Transparent while the content is at the top; the page's background once content scrolls
    /// under the bar, faded in over <c>HeaderBarConstants.ScrollEdgeFadeLength</c> points.
    /// </summary>
    Solid,

    /// <summary>Nothing: content shows through the bar at every offset.</summary>
    Clear,
}

/// <summary>The colour of the status bar's clock and icons while a page is shown.</summary>
public enum StatusBarStyle
{
    /// <summary>Follows the app theme: dark content on a light theme, light content on a dark one.</summary>
    Default,

    /// <summary>Light (white) clock and icons, for a page whose top is dark or a photo.</summary>
    LightContent,

    /// <summary>Dark (black) clock and icons, for a page whose top is light.</summary>
    DarkContent,
}
