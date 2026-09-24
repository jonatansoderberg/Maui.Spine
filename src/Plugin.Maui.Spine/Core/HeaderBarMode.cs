namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Where a page's content starts under Spine's header bar. What is behind the bar when content is
/// under it is <see cref="HeaderBarBackground"/>.
/// </summary>
public enum HeaderBarMode
{
    /// <summary>
    /// The content starts below the bar. When it scrolls under the bar (a large title, or a
    /// <see cref="HeaderBarBackground.Transparent"/> or scroll edge background), Spine insets the
    /// page's scroll view so its first row still starts below the bar at rest.
    /// </summary>
    Normal,

    /// <summary>
    /// The content starts at the top of the screen, behind the status bar and the bar: for pages
    /// that open on a photo, a map or a hero. The page keeps clear what it wants to:
    /// <see cref="ViewModelBase.SafeAreaInsets"/> reports the height of the status bar plus the bar,
    /// and a list takes it with <c>SafeArea.ScrollInset="Top"</c>. The title and the actions keep a
    /// fixed colour through <c>HeaderBarForeground</c>.
    /// </summary>
    Overlay,
}

/// <summary>
/// What is behind the header bar's title and actions when content is under the bar: content
/// scrolled under it, or the top of an <see cref="HeaderBarMode.Overlay"/> page. With nothing under
/// the bar, every value looks the same.
/// </summary>
public enum HeaderBarBackground
{
    /// <summary>
    /// What the platform's own bar does. On iOS and Mac Catalyst that is the navigation bar's default:
    /// <see cref="SmoothEdge"/> on 26 and <see cref="HardEdge"/> from 27, for a region or tab page
    /// whose scroll view fills it from the top (a page with fixed content above its list gets
    /// <see cref="Solid"/>, so that content never sits under the bar). Everywhere else it is
    /// <see cref="Solid"/>. Under <see cref="HeaderBarMode.Overlay"/> it is
    /// <see cref="Transparent"/>: the page draws its own top.
    /// </summary>
    Auto,

    /// <summary>
    /// The page's background colour: content under the bar is hidden. Under
    /// <see cref="HeaderBarMode.Normal"/> the bar takes its own row; over content that starts under
    /// the bar (Overlay, a large title), the colour fades in over the first
    /// <c>HeaderBarConstants.ScrollEdgeFadeLength</c> points of scroll.
    /// </summary>
    Solid,

    /// <summary>
    /// Nothing: content shows through the bar at every offset, the title and the actions float over
    /// it. For a photo or a map under an <see cref="HeaderBarMode.Overlay"/> bar.
    /// </summary>
    Transparent,

    /// <summary>
    /// Content under the header fades and blurs into it: UIKit's scroll edge effect with the soft
    /// style, over the status bar and the whole bar, as behind a navigation bar on iOS 26. Android
    /// and Windows show a band in the page's colour, slightly see-through behind the bar and fading
    /// out below it. iOS and Mac Catalyst before 26, and Reduce Transparency, give <see cref="Solid"/>.
    /// </summary>
    SmoothEdge,

    /// <summary>
    /// <see cref="SmoothEdge"/> behind the status bar only: content fades out under the clock and the
    /// icons, and stays sharp behind the title and the actions. Android and Windows show the band
    /// behind the status bar only.
    /// </summary>
    SmoothStatusBar,

    /// <summary>
    /// UIKit's scroll edge effect with the hard style: a frosted, nearly opaque band that ends in a
    /// clear edge, as behind a navigation bar from iOS 27. Android and Windows show the page's colour,
    /// nearly opaque, down to the bar's bottom edge, with a hairline there.
    /// </summary>
    HardEdge,
}

internal static class HeaderBarBackgroundExtensions
{
    /// <summary>Whether <paramref name="background"/> is one of the scroll edge values.</summary>
    public static bool IsScrollEdge(this HeaderBarBackground background) =>
        background is HeaderBarBackground.SmoothEdge or HeaderBarBackground.SmoothStatusBar or HeaderBarBackground.HardEdge;
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
