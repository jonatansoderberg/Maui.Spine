using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Look of a <see cref="SpineSection"/>. Set on one section through
/// <see cref="SpineSection.StyleOptions"/>, or app-wide as a resource keyed
/// <c>DefaultSpineSectionStyleOptions</c>; see <see cref="SpineStyleOptions{TSelf}"/> for the chain.
/// </summary>
/// <remarks>
/// The defaults follow each platform's settings screen. iOS: a filled group with continuous
/// 26-point corners (10 before iOS 26), hairline separators from the text to 16 points before the
/// edge, a bold 17-point grey header (13 points before iOS 26) and a 13-point footer; the page behind should be
/// <see cref="SpineSection.PageBackgroundLight"/> / <see cref="SpineSection.PageBackgroundDark"/>.
/// Android: no group and no separators, a header in the accent colour, as in Android's settings.
/// Windows: each row a card of its own, 4 points apart, under a bold header in the text colour, as in Windows'
/// Settings (<see cref="SeparateRows"/>).
/// </remarks>
public class SpineSectionStyleOptions : SpineStyleOptions<SpineSectionStyleOptions>
{
    static readonly bool IsAndroid = DeviceInfo.Platform == DevicePlatform.Android;
    static readonly bool IsWindows = DeviceInfo.Platform == DevicePlatform.WinUI;
    static readonly bool IsModernApple = OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26);

    /// <summary>Font of the header and the footer. <see langword="null"/> = the app's default font.</summary>
    public string? FontFamily { get; set; }

    // iOS 26 Settings: the header as large as a row's title, bold, in the secondary label colour.
    public double HeaderFontSize { get; set; } = IsAndroid ? 14 : IsWindows ? 14 : IsModernApple ? 17 : 13;
    public FontAttributes HeaderFontAttributes { get; set; } = IsAndroid || IsWindows || IsModernApple ? FontAttributes.Bold : FontAttributes.None;
    public double FooterFontSize { get; set; } = IsAndroid ? 14 : IsWindows ? 12 : 13;

    /// <summary>Corner radius of the group, or of each row's card with <see cref="SeparateRows"/>.</summary>
    public double CornerRadius { get; set; } = IsAndroid ? 0 : IsWindows ? 4 : IsModernApple ? 26 : 10;

    /// <summary>
    /// Draws each row as a card of its own, <see cref="RowSpacing"/> apart and without separators, as Windows'
    /// Settings does. On by default on Windows.
    /// </summary>
    public bool SeparateRows { get; set; } = IsWindows;

    /// <summary>Space between the rows' cards (<see cref="SeparateRows"/>).</summary>
    public double RowSpacing { get; set; } = 4;

    /// <summary>A 1-point stroke around the group or each card; <see langword="null"/> = none (the default except on Windows).</summary>
    public Color? BorderColor { get; set; }

    /// <summary>Whether separators are drawn between the rows. Android's lists use none.</summary>
    public bool ShowSeparators { get; set; } = !IsAndroid;

    /// <summary>Space between a separator's end and the group's trailing edge.</summary>
    public double SeparatorTrailingInset { get; set; } = 16;

    /// <summary>Space before the header and footer text, from the group's edge. On Windows the text lines up with the cards.</summary>
    public double TextInset { get; set; } = IsWindows ? 1 : 16;

    /// <summary>Fill of the group. <see cref="Colors.Transparent"/> on Android: the rows sit on the page.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Colour of the separators. Keep it opaque: an alpha colour on a BoxView paints over black on iOS.</summary>
    public Color? SeparatorColor { get; set; }

    public Color? HeaderColor { get; set; }
    public Color? FooterColor { get; set; }

    protected override void InheritColorsFrom(SpineSectionStyleOptions source)
    {
        BackgroundColor ??= source.BackgroundColor;
        BorderColor ??= source.BorderColor;
        SeparatorColor ??= source.SeparatorColor;
        HeaderColor ??= source.HeaderColor;
        FooterColor ??= source.FooterColor;
    }

    protected override void ApplyThemeDefaults(AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;
        var secondary = Color.FromArgb(dark ? "#8D8D93" : "#6D6D72");

        // secondarySystemGroupedBackground and opaqueSeparator on Apple; a card on Windows.
        BackgroundColor ??= IsAndroid ? Colors.Transparent
            : IsWindows ? Color.FromArgb(dark ? "#2B2B2B" : "#FBFBFB")
            : Color.FromArgb(dark ? "#1C1C1E" : "#FFFFFF");
        // Windows' card stroke: a shade darker than the card.
        if (IsWindows)
            BorderColor ??= Color.FromArgb(dark ? "#1D1D1D" : "#E5E5E5");
        SeparatorColor ??= Color.FromArgb(dark ? "#38383A" : "#C6C6C8");
        HeaderColor ??= IsAndroid ? SpineTheme.GetAccent(theme) ?? secondary
            : IsWindows ? (dark ? Colors.White : Colors.Black)
            : secondary;
        FooterColor ??= secondary;
    }
}
