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
    /// own top. On iOS and Mac Catalyst 26, <see cref="ScrollEdge"/> for a region or tab page whose
    /// scroll view fills it from the top (so nothing that does not scroll ends up under the bar);
    /// <see cref="Solid"/> otherwise.
    /// </summary>
    Auto,

    /// <summary>
    /// Transparent while the content is at the top; the page's background once content scrolls
    /// under the bar, faded in over <c>HeaderBarConstants.ScrollEdgeFadeLength</c> points.
    /// </summary>
    Solid,

    /// <summary>Nothing: content shows through the bar at every offset.</summary>
    Clear,

    /// <summary>
    /// Content scrolls under the bar and stays half visible behind it: the iOS 26 scroll edge
    /// effect, drawn by UIKit as it does for a navigation bar, over the status bar and the whole
    /// bar. Lays the page out under the bar like <see cref="HeaderBarMode.Overlay"/>. The style is
    /// UIKit's automatic one, as for a navigation bar: soft on iPhone; the system may choose hard
    /// elsewhere, such as on the Mac. Where
    /// the system effect does not exist (Android, Windows) the stand-in for
    /// <see cref="ScrollEdgeSoft"/> is shown; on iOS and Mac Catalyst before 26, and with Reduce
    /// Transparency on, it is <see cref="Solid"/>.
    /// </summary>
    ScrollEdge,

    /// <summary>
    /// <see cref="ScrollEdge"/> with the soft style: content fades and blurs into the bar. Android
    /// and Windows show a band in the page's background colour, slightly see-through behind the
    /// bar and fading out below it.
    /// </summary>
    ScrollEdgeSoft,

    /// <summary>
    /// <see cref="ScrollEdge"/> with the hard style: a frosted, nearly opaque band behind the bar
    /// with a hairline at its bottom edge, for a bar with more in it than a title. Android and
    /// Windows show the page's background colour, nearly opaque, down to the bar's bottom edge,
    /// with a hairline there.
    /// </summary>
    ScrollEdgeHard,
}

internal static class HeaderBarBackgroundExtensions
{
    /// <summary>Whether <paramref name="background"/> is one of the scroll edge values.</summary>
    public static bool IsScrollEdge(this HeaderBarBackground background) =>
        background is HeaderBarBackground.ScrollEdge or HeaderBarBackground.ScrollEdgeSoft or HeaderBarBackground.ScrollEdgeHard;
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
