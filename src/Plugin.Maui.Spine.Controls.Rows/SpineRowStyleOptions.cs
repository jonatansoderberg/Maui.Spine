using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Fonts, sizes and colours of a <see cref="SpineRow"/>. Set on one row through
/// <see cref="SpineRow.StyleOptions"/>, or app-wide as a resource keyed
/// <c>DefaultSpineRowStyleOptions</c>; see <see cref="SpineStyleOptions{TSelf}"/> for the chain.
/// </summary>
/// <remarks>
/// A colour left <see langword="null"/> follows the theme. Sizes default to each platform's own
/// list rows: 44-point rows and 17-point text on iOS, 56 dp and 16 on Android.
/// </remarks>
public class SpineRowStyleOptions : SpineStyleOptions<SpineRowStyleOptions>
{
    static readonly bool IsAndroid = DeviceInfo.Platform == DevicePlatform.Android;
    static readonly bool IsWindows = DeviceInfo.Platform == DevicePlatform.WinUI;

    /// <summary>Font of the row's text. <see langword="null"/> = the app's default font.</summary>
    public string? FontFamily { get; set; }

    public double TitleFontSize { get; set; } = IsAndroid ? 16 : IsWindows ? 14 : 17;
    public double DetailFontSize { get; set; } = IsAndroid ? 14 : IsWindows ? 12 : 13;
    public double ValueFontSize { get; set; } = IsAndroid ? 16 : IsWindows ? 14 : 17;
    public FontAttributes TitleFontAttributes { get; set; }

    public double IconSize { get; set; } = 28;
    public double MinimumHeight { get; set; } = IsAndroid ? 56 : IsWindows ? 40 : 44;
    public Thickness Padding { get; set; } = new(16, 8);
    public double Spacing { get; set; } = IsAndroid ? 16 : 12;

    /// <summary>Opacity of a disabled row's content.</summary>
    public double DisabledOpacity { get; set; } = 0.4;

    public Color? TitleColor { get; set; }
    public Color? DetailColor { get; set; }
    public Color? ValueColor { get; set; }

    /// <summary>Tint of the icon. <see cref="Colors.Transparent"/> keeps the SVG's own colours.</summary>
    public Color? IconColor { get; set; }

    public Color? ChevronColor { get; set; }

    protected override void InheritColorsFrom(SpineRowStyleOptions source)
    {
        TitleColor ??= source.TitleColor;
        DetailColor ??= source.DetailColor;
        ValueColor ??= source.ValueColor;
        IconColor ??= source.IconColor;
        ChevronColor ??= source.ChevronColor;
    }

    protected override void ApplyThemeDefaults(AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;

        // The system label colours, opaque: secondary for detail and value, tertiary for the chevron.
        TitleColor ??= dark ? Colors.White : Colors.Black;
        DetailColor ??= Color.FromArgb(dark ? "#98989F" : "#8A8A8E");
        ValueColor ??= DetailColor;
        ChevronColor ??= Color.FromArgb(dark ? "#5A5A5F" : "#C4C4C7");
        IconColor ??= SpineTheme.GetAccent(theme) ?? Color.FromArgb(dark ? "#0A84FF" : "#007AFF");
    }
}
