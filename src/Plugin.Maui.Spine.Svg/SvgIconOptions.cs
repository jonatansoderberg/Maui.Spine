namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Options that control how SVGs are rendered and stored by <see cref="ISvgIconService"/>.
/// Configure via <c>builder.UseSvgIcon(options => ...)</c> in <c>MauiProgram.cs</c>.
/// </summary>
public sealed class SvgIconOptions
{
    /// <summary>
    /// Pixel sizes included in the generated Windows ICO file.
    /// Defaults to the standard set used by Windows shell icons.
    /// </summary>
    public int[] IcoSizes { get; set; } = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    /// <summary>
    /// Directory where generated icon files are cached on disk.
    /// Defaults to <c>%LOCALAPPDATA%/SvgIconCache</c> (or the platform equivalent).
    /// </summary>
    public string CacheDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SvgIconCache");

    /// <summary>
    /// Template used to build icon file names (without extension or size suffix).
    /// Supported placeholders: <c>{name}</c> (logical icon name) and <c>{hash}</c> (content hash).
    /// PNG files additionally append <c>_{size}px</c> before the extension.
    /// Defaults to <c>"{name}_{hash}"</c>.
    /// </summary>
    public string FileNameTemplate { get; set; } = "{name}_{hash}";

    /// <summary>
    /// Uniform padding applied on each side of the SVG canvas, expressed as a fraction
    /// of the icon size (e.g. <c>0.1</c> = 10 % padding on each side).
    /// Defaults to <c>0.0</c> (no padding).
    /// </summary>
    public float PaddingPercent { get; set; } = 0.0f;

    /// <summary>
    /// Multiplier applied to all stroke widths in the SVG before rendering.
    /// <c>1.0</c> = no change; <c>2.0</c> = double every stroke width (e.g. 1 → 2, 2 → 4).
    /// Defaults to <c>1.0</c>.
    /// </summary>
    public float LineWidthScale { get; set; } = 1.0f;

    /// <summary>
    /// Tint colour applied to the SVG when rendering with <see cref="Plugin.Maui.Spine.Svg.SvgTheme.Light"/>.
    /// An SVG that paints with <c>currentColor</c> takes the tint only there and keeps its other
    /// colours; any other SVG is tinted wherever it is drawn.
    /// Use <see cref="Colors.Transparent"/> to keep the original SVG colours.
    /// Defaults to <see cref="Colors.Black"/>.
    /// </summary>
    public Color LightTintColor { get; set; } = Colors.Black;

    /// <summary>
    /// Tint colour applied to the SVG when rendering with <see cref="Plugin.Maui.Spine.Svg.SvgTheme.Dark"/>.
    /// An SVG that paints with <c>currentColor</c> takes the tint only there and keeps its other
    /// colours; any other SVG is tinted wherever it is drawn.
    /// Use <see cref="Colors.Transparent"/> to keep the original SVG colours.
    /// Defaults to <see cref="Colors.White"/>.
    /// </summary>
    public Color DarkTintColor { get; set; } = Colors.White;

    /// <summary>
    /// Gives the SVG's own colours their dark tones in the <see cref="Plugin.Maui.Spine.Svg.SvgTheme.Dark"/>
    /// render, from <see cref="SvgImageOptions.DarkColors"/> or the automatic rule
    /// (<see cref="SvgImageOptions.DarkColorMuting"/>). The tint is left as it is. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AdjustColorsForDark { get; set; }

    /// <summary>
    /// PNG encoding quality in the range 0–100.
    /// Defaults to <c>100</c>.
    /// </summary>
    public int PngQuality { get; set; } = 100;

    /// <summary>
    /// Point size of the page used when generating the macOS PDF icon.
    /// 18 pt matches the standard macOS menu-bar (@1x) icon size.
    /// Defaults to <c>18</c>.
    /// </summary>
    public int MacOsPdfSize { get; set; } = 18;
}
