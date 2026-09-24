namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// The header bar's measurements per platform, public so a page that draws something in line with
/// the bar (a large title, a hand-drawn row) uses Spine's numbers instead of copies.
/// </summary>
public static class HeaderBarConstants
{
    /// <summary>The back button's glyph, an embedded SVG; a row's chevron is the same glyph turned around.</summary>
    public const string BackGlyph = "chevronleft.svg";

    /// <summary>Width that lets a header button size to its text when it has no icon.</summary>
    public const double Auto = -1;

    /// <summary>How long a header bar action takes to fade in, in milliseconds.</summary>
    public const uint FadeInDuration = 60;
    /// <summary>How long a header bar action takes to fade out, in milliseconds.</summary>
    public const uint FadeOutDuration = 90;

    /// <summary>
    /// Where a <see cref="Core.NavigableAttribute.LargeTitle"/> page puts its large title, as the
    /// first thing in its scroll content: side margins, and nothing above (the scroll inset already
    /// starts it under the bar).
    /// </summary>
    public static readonly Thickness LargeTitleMargin = new(LargeTitleSideMargin, 0, LargeTitleSideMargin, 0);

    /// <summary>
    /// Scroll distance over which the header bar's title fades in, ending at the collapse distance
    /// (by default <see cref="LargeTitleCollapseDistance"/>): the large title's text passing under
    /// the bar.
    /// </summary>
    public const double LargeTitleFadeLength = 20;

    /// <summary>
    /// Scroll distance over which the bar background goes from transparent to solid once content
    /// starts passing under it.
    /// </summary>
    public const double ScrollEdgeFadeLength = 12;


#if ANDROID

    // Button height (shared across sheet and region presentations)
    /// <summary>Height of the header bar's item row and of its buttons.</summary>
    public const double Height = 48;

    // Sheet presentation button size
    /// <summary>Width of a header bar button in a sheet.</summary>
    public const double SheetButtonWidth = 48;
    /// <summary>Padding inside a header bar button in a sheet.</summary>
    public const double SheetButtonPadding = 8;

    // Region presentation button size
    /// <summary>Width of a header bar button on a region page.</summary>
    public const double RegionButtonWidth = 48;
    /// <summary>Padding inside a header bar button on a region page.</summary>
    public const double RegionButtonPadding = 8;

    /// <summary>Space between the screen edge and the outermost header bar button on a region page.</summary>
    public const double RegionSideMargin = 4;
    /// <summary>Space between the sheet edge and the outermost header bar button in a sheet.</summary>
    public const double SheetSideMargin = 10;
    /// <summary>Space between a sheet's top edge and its header bar.</summary>
    public const double SheetTopPadding = 0;

    // Material 3 medium top app bar: headline small (24 sp, regular) below the 48-point row.
    /// <summary>Font size of a page's large title.</summary>
    public const double LargeTitleFontSize = 24;
    /// <summary>Weight of a page's large title.</summary>
    public const FontAttributes LargeTitleFontAttributes = FontAttributes.None;
    /// <summary>Height of the large title's row.</summary>
    public const double LargeTitleHeight = 56;
    /// <summary>Left and right margin of the large title.</summary>
    public const double LargeTitleSideMargin = 16;

#elif IOS || MACCATALYST

    // The UINavigationBar item size: a 44-point row, and 44-point circles for icon actions. The
    // bar itself can be taller, see BarHeight.
    /// <summary>Height of the header bar's item row and of its buttons.</summary>
    public const double Height = 44;

    // Sheet presentation button size
    /// <summary>Width of a header bar button in a sheet.</summary>
    public const double SheetButtonWidth = 44;
    /// <summary>Padding inside a header bar button in a sheet.</summary>
    public const double SheetButtonPadding = 0;

    // Region presentation button size
    /// <summary>Width of a header bar button on a region page.</summary>
    public const double RegionButtonWidth = 48;
    /// <summary>Padding inside a header bar button on a region page.</summary>
    public const double RegionButtonPadding = 0;

    /// <summary>Space between the screen edge and the outermost header bar button on a region page.</summary>
    public const double RegionSideMargin = 8;
    /// <summary>Space between the sheet edge and the outermost header bar button in a sheet.</summary>
    public const double SheetSideMargin = 16;

    // Space below the UISheetPresentationController grabber handle
    /// <summary>Space between a sheet's top edge and its header bar.</summary>
    public const double SheetTopPadding = 20;

    // UINavigationBar's large title: 34-point bold in a 52-point row below the bar.
    /// <summary>Font size of a page's large title.</summary>
    public const double LargeTitleFontSize = 34;
    /// <summary>Weight of a page's large title.</summary>
    public const FontAttributes LargeTitleFontAttributes = FontAttributes.Bold;
    /// <summary>Height of the large title's row.</summary>
    public const double LargeTitleHeight = 52;
    /// <summary>Left and right margin of the large title.</summary>
    public const double LargeTitleSideMargin = 16;

#else

    // Button height (shared across sheet and region presentations)
    /// <summary>Height of the header bar's item row and of its buttons.</summary>
    public const double Height = 32;

    // Sheet presentation button size
    /// <summary>Width of a header bar button in a sheet.</summary>
    public const double SheetButtonWidth = 32;
    /// <summary>Padding inside a header bar button in a sheet.</summary>
    public const double SheetButtonPadding = 0;

    // Region presentation button size (width differs on desktop)
    /// <summary>Width of a header bar button on a region page.</summary>
    public const double RegionButtonWidth = 48;
    /// <summary>Padding inside a header bar button on a region page.</summary>
    public const double RegionButtonPadding = 0;

    /// <summary>Space between the screen edge and the outermost header bar button on a region page.</summary>
    public const double RegionSideMargin = 0;
    /// <summary>Space between the sheet edge and the outermost header bar button in a sheet.</summary>
    public const double SheetSideMargin = 16;
    /// <summary>Space between a sheet's top edge and its header bar.</summary>
    public const double SheetTopPadding = 0;

    // WinUI's title-large text style.
    /// <summary>Font size of a page's large title.</summary>
    public const double LargeTitleFontSize = 28;
    /// <summary>Weight of a page's large title.</summary>
    public const FontAttributes LargeTitleFontAttributes = FontAttributes.Bold;
    /// <summary>Height of the large title's row.</summary>
    public const double LargeTitleHeight = 48;
    /// <summary>Left and right margin of the large title.</summary>
    public const double LargeTitleSideMargin = 16;

#endif

    /// <summary>
    /// The header bar's height below the status bar: the <see cref="Height"/> row that holds the
    /// title and the actions, plus, on iOS and Mac Catalyst 26 and later, 10 points of bar below
    /// it. That is a <c>UINavigationBar</c> there: 54 points with its 44-point items at the top
    /// (44 points on earlier versions, in a sheet as well). Content below the bar, the scroll
    /// inset of content under a floating bar, a solid bar background and the scroll edge effect
    /// all start or end this far below the status bar. Equal to <see cref="Height"/> elsewhere.
    /// </summary>
    public static double BarHeight { get; } =
#if IOS || MACCATALYST
        OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26) ? 54 : Height;
#else
        Height;
#endif

    /// <summary>
    /// The scroll offset at which a large title laid out with these constants has gone under the
    /// bar: its text is centred in the row, so the text's lower edge is half a row plus half a font
    /// size down. The default <c>HeaderBar.CollapseDistance</c>.
    /// </summary>
    public const double LargeTitleCollapseDistance = (LargeTitleHeight + LargeTitleFontSize) / 2;
}
