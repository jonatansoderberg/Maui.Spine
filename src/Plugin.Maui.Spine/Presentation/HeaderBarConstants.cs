namespace Plugin.Maui.Spine.Presentation;

internal static class HeaderBarConstants
{
    // Use -1 to allow width to size to text content when no SVG is present
    public const double Auto = -1;

    // Header bar animation durations (ms)
    public const uint FadeInDuration = 60;
    public const uint FadeOutDuration = 90;

    // Outer column widths that act as left/right margins of the header bar
    

#if ANDROID

    // Button height (shared across sheet and region presentations)
    public const double Height = 48;

    // Sheet presentation button size
    public const double SheetButtonWidth = 48;
    public const double SheetButtonPadding = 8;

    // Region presentation button size
    public const double RegionButtonWidth = 48;
    public const double RegionButtonPadding = 8;

    public const double RegionSideMargin = 4;
    public const double SheetSideMargin = 10;
    public const double SheetTopPadding = 0;

#elif IOS || MACCATALYST

    // The UINavigationBar item size: a 44-point row, and 44-point circles for icon actions.
    public const double Height = 44;

    // Sheet presentation button size
    public const double SheetButtonWidth = 44;
    public const double SheetButtonPadding = 0;

    // Region presentation button size
    public const double RegionButtonWidth = 48;
    public const double RegionButtonPadding = 0;

    public const double RegionSideMargin = 8;
    public const double SheetSideMargin = 16;

    // Space below the UISheetPresentationController grabber handle
    public const double SheetTopPadding = 20;

#else

    // Button height (shared across sheet and region presentations)
    public const double Height = 32;

    // Sheet presentation button size
    public const double SheetButtonWidth = 32;
    public const double SheetButtonPadding = 0;

    // Region presentation button size (width differs on desktop)
    public const double RegionButtonWidth = 48;
    public const double RegionButtonPadding = 0;

    public const double RegionSideMargin = 0;
    public const double SheetSideMargin = 16;
    public const double SheetTopPadding = 0;

#endif

}
