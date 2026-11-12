using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Fonts, sizes and colours of a <see cref="SpineRow"/>. Set on one row through
/// <see cref="SpineRow.StyleOptions"/>, or app-wide as a resource keyed
/// <c>DefaultSpineRowStyleOptions</c>; see <see cref="SpineStyleOptions{TSelf}"/> for the chain.
/// </summary>
/// <remarks>
/// A colour left <see langword="null"/> follows the theme. Sizes default to each platform's own
/// list rows: on iOS 26 the 52-point rows of Settings (44 before), 17-point text and a 30-point
/// icon badge; on Android the Material 3 list item, 56 dp for one line and 72 dp for two, 16 and
/// 14 sp text and a 24 dp icon; on Windows the settings card, 14 and 12 point text, a 20-point glyph in the
/// text colour instead of a badge and the status as a dot before the detail.
/// </remarks>
public class SpineRowStyleOptions : SpineStyleOptions<SpineRowStyleOptions>
{
    static readonly bool IsAndroid = DeviceInfo.Platform == DevicePlatform.Android;
    static readonly bool IsWindows = DeviceInfo.Platform == DevicePlatform.WinUI;
    static readonly bool IsModernApple = OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26);

    /// <summary>Font of the row's text. <see langword="null"/> = the app's default font.</summary>
    public string? FontFamily { get; set; }

    public double TitleFontSize { get; set; } = IsAndroid ? 16 : IsWindows ? 14 : 17;
    public double DetailFontSize { get; set; } = IsAndroid ? 14 : IsWindows ? 12 : 13;
    public double ValueFontSize { get; set; } = IsAndroid ? 16 : IsWindows ? 14 : 17;
    public FontAttributes TitleFontAttributes { get; set; }

    /// <summary>Size of the icon when it has no <see cref="SpineRow.IconBackground"/>.</summary>
    public double IconSize { get; set; } = IsAndroid ? 24 : IsWindows ? 26 : 28;

    /// <summary>
    /// Margin of the SVG inside the icon's square when it has no badge. <see langword="null"/> = the canvas' own (5);
    /// 1 on Windows (in a 26-point square), so the glyph is as large as the 20-point icons of its Settings.
    /// </summary>
    public double? IconPadding { get; set; } = IsWindows ? 1 : null;

    /// <summary>
    /// Stroke of the icon when it has no badge, relative to the glyph's own. <see langword="null"/> = as drawn; 1.5 on
    /// Windows, whose Settings icons are drawn heavier, so they read white rather than grey.
    /// </summary>
    public double? IconLineWidthScale { get; set; } = IsWindows ? 1.25 : null;

    /// <summary>
    /// Whether <see cref="SpineRow.IconBackground"/> draws a badge behind the icon. Off on Windows, whose settings draw
    /// a plain glyph in the text colour (<see cref="IconColor"/>); the background colour is then not used.
    /// </summary>
    public bool ShowIconBadges { get; set; } = !IsWindows;

    /// <summary>Space between the icon's square and the text: on Windows 15, so the text starts 60 points into the card as in its Settings.</summary>
    public double IconSpacing { get; set; } = IsAndroid ? 16 : IsWindows ? 15 : 12;

    /// <summary>
    /// Shows <see cref="SpineRow.Status"/> as a small dot before the detail line, the way macOS and Windows mark a
    /// network or a device as connected, rather than a mark at the end of the row. On by default on the Mac and
    /// Windows; iOS keeps the mark, and so does a row without a detail.
    /// </summary>
    public bool StatusAsDot { get; set; } = IsWindows || DeviceInfo.Platform == DevicePlatform.MacCatalyst;

    /// <summary>
    /// Writes "On" or "Off" (<c>Spine.Row.On</c> / <c>Spine.Row.Off</c>) before a <see cref="Switch"/> accessory, in the
    /// title's font, as Windows Settings does. On by default on Windows; a row with a <see cref="SpineRow.Value"/> keeps it.
    /// </summary>
    public bool ShowSwitchState { get; set; } = IsWindows;

    /// <summary>
    /// Shows a <see cref="SpineRow.Menu"/> row's choice in a combo box (<see cref="DropDownColor"/>, a chevron pointing down)
    /// instead of the value and the up-and-down chevron, as Windows Settings does. On by default on Windows.
    /// </summary>
    public bool MenuAsDropDown { get; set; } = IsWindows;

    /// <summary>Fill of the combo box (<see cref="MenuAsDropDown"/>): Windows' ControlFillColorDefault.</summary>
    public Color? DropDownColor { get; set; }

    /// <summary>Stroke of the combo box: Windows' ControlStrokeColorDefault.</summary>
    public Color? DropDownBorderColor { get; set; }

    /// <summary>Diameter of the status dot (<see cref="StatusAsDot"/>).</summary>
    public double StatusDotSize { get; set; } = IsWindows ? 7 : 8;

    /// <summary>Size of the filled shape behind the icon when the row has an <see cref="SpineRow.IconBackground"/>.</summary>
    public double IconBadgeSize { get; set; } = IsAndroid ? 40 : 30;

    /// <summary>Size of the icon on its badge. It may be larger than the badge: the icon set draws with a margin, and the row gives up that margin rather than draw outside the badge.</summary>
    public double IconBadgeGlyphSize { get; set; } = IsAndroid ? 32 : 34;

    /// <summary>Corner radius of the badge: a rounded square on iOS and Windows, a circle on Android.</summary>
    public double IconBadgeCornerRadius { get; set; } = IsAndroid ? 20 : 7;

    /// <summary>Height of a row with one line of text.</summary>
    public double MinimumHeight { get; set; } = IsAndroid ? 56 : IsWindows ? 69 : IsModernApple ? 52 : 44;

    /// <summary>Height of a row with a second line (a detail, or the value under the title).</summary>
    public double TwoLineMinimumHeight { get; set; } = IsAndroid ? 72 : IsWindows ? 69 : IsModernApple ? 52 : 44;

    public Thickness Padding { get; set; } = new(16, 8);
    public double Spacing { get; set; } = IsAndroid ? 16 : 12;

    /// <summary>
    /// Stroke of the chevron and the check mark, relative to the glyph's own. Heavier than an icon,
    /// like the system's <c>chevron.forward</c>.
    /// </summary>
    public double GlyphLineWidthScale { get; set; } = IsWindows ? 2 : 1.6;

    /// <summary>Opacity of a disabled row's content.</summary>
    public double DisabledOpacity { get; set; } = 0.4;

    public Color? TitleColor { get; set; }
    public Color? DetailColor { get; set; }
    public Color? ValueColor { get; set; }

    /// <summary>Tint of the icon. <see cref="Colors.Transparent"/> keeps the SVG's own colours.</summary>
    public Color? IconColor { get; set; }

    /// <summary>Tint of the icon on its <see cref="SpineRow.IconBackground"/>.</summary>
    public Color? IconBadgeGlyphColor { get; set; }

    public Color? ChevronColor { get; set; }

    /// <summary>Colour of the check mark of a selected row.</summary>
    public Color? CheckColor { get; set; }

    /// <summary>Title colour of a row with <see cref="SpineRow.IsDestructive"/>: the system red, the error colour on Android.</summary>
    public Color? DestructiveColor { get; set; }

    /// <summary>Diameter of the <see cref="SpineRow.Status"/> mark.</summary>
    public double StatusSize { get; set; } = IsAndroid ? 24 : 22;

    /// <summary>Fill of the <see cref="SpineRowStatus.Ok"/> mark: the system green.</summary>
    public Color? StatusOkColor { get; set; }

    /// <summary>Fill of the <see cref="SpineRowStatus.NeedsAttention"/> mark: the system orange.</summary>
    public Color? StatusAttentionColor { get; set; }

    /// <summary>Colour of the check and the "!" on the status mark.</summary>
    public Color? StatusGlyphColor { get; set; }

    protected override void InheritColorsFrom(SpineRowStyleOptions source)
    {
        TitleColor ??= source.TitleColor;
        DetailColor ??= source.DetailColor;
        ValueColor ??= source.ValueColor;
        IconColor ??= source.IconColor;
        IconBadgeGlyphColor ??= source.IconBadgeGlyphColor;
        ChevronColor ??= source.ChevronColor;
        CheckColor ??= source.CheckColor;
        DestructiveColor ??= source.DestructiveColor;
        StatusOkColor ??= source.StatusOkColor;
        StatusAttentionColor ??= source.StatusAttentionColor;
        StatusGlyphColor ??= source.StatusGlyphColor;
        DropDownColor ??= source.DropDownColor;
        DropDownBorderColor ??= source.DropDownBorderColor;
    }

    protected override void ApplyThemeDefaults(AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;
        var accent = SpineTheme.GetAccent(theme) ?? Color.FromArgb(dark ? "#0A84FF" : "#007AFF");

        // The system label colours, opaque: secondary for detail and value, tertiary for the chevron.
        TitleColor ??= dark ? Colors.White : Colors.Black;
        if (IsWindows)
        {
            // Windows' text fill colours: secondary text, and glyphs in the text colour; success and caution fills.
            DetailColor ??= Color.FromArgb(dark ? "#CFCFCF" : "#616161");
            ChevronColor ??= TitleColor;
            IconColor ??= TitleColor;
            StatusOkColor ??= Color.FromArgb(dark ? "#6CCB5F" : "#0F7B0F");
            StatusAttentionColor ??= Color.FromArgb(dark ? "#FCE100" : "#9D5D00");
        }

        DetailColor ??= Color.FromArgb(dark ? "#98989F" : "#8A8A8E");
        ValueColor ??= DetailColor;
        ChevronColor ??= Color.FromArgb(dark ? "#5A5A5F" : "#C4C4C7");
        IconColor ??= accent;
        IconBadgeGlyphColor ??= Colors.White;
        CheckColor ??= accent;
        DestructiveColor ??= Color.FromArgb(IsAndroid ? (dark ? "#F2B8B5" : "#B3261E") : (dark ? "#FF453A" : "#FF3B30"));
        StatusOkColor ??= Color.FromArgb(dark ? "#30D158" : "#34C759");
        StatusAttentionColor ??= Color.FromArgb(dark ? "#FF9F0A" : "#FF9500");
        StatusGlyphColor ??= Colors.White;
        DropDownColor ??= Color.FromArgb(dark ? "#0FFFFFFF" : "#B3FFFFFF");
        DropDownBorderColor ??= Color.FromArgb(dark ? "#12FFFFFF" : "#0F000000");
    }
}
