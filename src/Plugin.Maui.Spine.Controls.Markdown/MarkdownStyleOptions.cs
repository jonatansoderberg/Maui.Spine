using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Fonts, sizes and colours of a <see cref="MarkdownView"/>. Set on one view through
/// <see cref="MarkdownView.StyleOptions"/>, or app-wide as a resource keyed <c>DefaultMarkdownStyleOptions</c>; see
/// <see cref="SpineStyleOptions{TSelf}"/> for the chain.
/// </summary>
/// <remarks>A colour left <see langword="null"/> follows the theme; the link colour follows the app's accent.</remarks>
public class MarkdownStyleOptions : SpineStyleOptions<MarkdownStyleOptions>
{
    static readonly bool IsAndroid = DeviceInfo.Platform == DevicePlatform.Android;
    static readonly bool IsWindows = DeviceInfo.Platform == DevicePlatform.WinUI;

    /// <summary>Font of the text. <see langword="null"/> = the app's default font.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Font of the headings. <see langword="null"/> = <see cref="FontFamily"/>.</summary>
    public string? HeadingFontFamily { get; set; }

    /// <summary>Size of body text: the platform's body size.</summary>
    public double FontSize { get; set; } = IsAndroid ? 16 : IsWindows ? 14 : 17;

    /// <summary>Heading sizes for levels 1 to 6, as multiples of <see cref="FontSize"/>.</summary>
    public double[] HeadingScales { get; set; } = [1.6, 1.35, 1.2, 1.05, 1.0, 0.9];

    /// <summary>Heading weight.</summary>
    public FontAttributes HeadingFontAttributes { get; set; } = FontAttributes.Bold;

    /// <summary>Font of code. <see langword="null"/> = the platform's monospace font.</summary>
    public string? CodeFontFamily { get; set; }

    /// <summary>Size of code, as a multiple of <see cref="FontSize"/>.</summary>
    public double CodeScale { get; set; } = 0.88;

    /// <summary>Space between blocks.</summary>
    public double BlockSpacing { get; set; } = 12;

    /// <summary>Indent of a list's items, and of each nested list.</summary>
    public double ListIndent { get; set; } = 22;

    /// <summary>Corner radius of code blocks.</summary>
    public double CodeCornerRadius { get; set; } = 10;

    /// <summary>Line height of body text, as a multiple of the font's own.</summary>
    public double LineHeight { get; set; } = 1.15;

    public Color? TextColor { get; set; }
    public Color? HeadingColor { get; set; }

    /// <summary>Links. <see langword="null"/> = the app's accent.</summary>
    public Color? LinkColor { get; set; }

    public Color? CodeBackground { get; set; }
    public Color? CodeTextColor { get; set; }

    /// <summary>The bar beside a quote.</summary>
    public Color? QuoteRuleColor { get; set; }

    /// <summary>Text in a quote.</summary>
    public Color? QuoteTextColor { get; set; }

    /// <summary>Lines between table cells, and a horizontal rule.</summary>
    public Color? RuleColor { get; set; }

    /// <summary>A table's header row.</summary>
    public Color? TableHeaderBackground { get; set; }

    internal string MonospaceFamily => CodeFontFamily ?? (IsAndroid ? "monospace" : IsWindows ? "Consolas" : "Menlo");

    internal double HeadingSize(int level) => FontSize * HeadingScales[Math.Clamp(level, 1, HeadingScales.Length) - 1];

    protected override void InheritColorsFrom(MarkdownStyleOptions source)
    {
        TextColor ??= source.TextColor;
        HeadingColor ??= source.HeadingColor;
        LinkColor ??= source.LinkColor;
        CodeBackground ??= source.CodeBackground;
        CodeTextColor ??= source.CodeTextColor;
        QuoteRuleColor ??= source.QuoteRuleColor;
        QuoteTextColor ??= source.QuoteTextColor;
        RuleColor ??= source.RuleColor;
        TableHeaderBackground ??= source.TableHeaderBackground;
    }

    protected override void ApplyThemeDefaults(AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;

        TextColor ??= dark ? Colors.White : Colors.Black;
        HeadingColor ??= TextColor;
        LinkColor ??= SpineTheme.GetAccent(theme) ?? Color.FromArgb(dark ? "#0A84FF" : "#007AFF");
        CodeBackground ??= Color.FromArgb(dark ? "#2C2C2E" : "#F2F2F7");
        CodeTextColor ??= TextColor;
        QuoteRuleColor ??= Color.FromArgb(dark ? "#48484A" : "#C7C7CC");
        QuoteTextColor ??= Color.FromArgb(dark ? "#AEAEB2" : "#6C6C70");
        RuleColor ??= Color.FromArgb(dark ? "#38383A" : "#D1D1D6");
        TableHeaderBackground ??= Color.FromArgb(dark ? "#1C1C1E" : "#F9F9FB");
    }
}
