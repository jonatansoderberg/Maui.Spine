namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// App-wide options for SVG images, set through
/// <see cref="MauiAppBuilderExtensions.UseEmbeddedSvgImages(MauiAppBuilder, Action{SvgImageOptions}, System.Reflection.Assembly[])"/>
/// in <c>MauiProgram</c>.
/// </summary>
public sealed class SvgImageOptions
{
    /// <summary>
    /// Whether images give the SVG's own colours their dark tones in the dark theme, for colours chosen
    /// for a light background. <c>SvgImageSource.AdjustColorsForDark</c> overrides it per image. The tint
    /// is never changed. Default <see langword="false"/>.
    /// </summary>
    public bool AdjustColorsForDark { get; set; }

    /// <summary>
    /// Exact dark tones, by the light colour an SVG uses: <c>DarkColors[Color.FromArgb("#E53935")] =
    /// Color.FromArgb("#C8625E")</c>. Colours match on red, green and blue; the SVG's alpha is kept.
    /// </summary>
    public IDictionary<Color, Color> DarkColors { get; } = new Dictionary<Color, Color>();

    /// <summary>
    /// For colours not in <see cref="DarkColors"/>: how much chroma they lose in the dark theme, from
    /// <c>0</c> (none) to <c>1</c> (grey), so a vivid red turns more matte. Colours darker than mid-grey
    /// are also lightened, keeping their hue. Default <c>0.15</c>.
    /// </summary>
    public double DarkColorMuting { get; set; } = 0.15;

    internal SvgDarkPalette ToPalette() => new(
        DarkColors.ToDictionary(static pair => Rgb(pair.Key), static pair => Rgb(pair.Value)),
        DarkColorMuting);

    private static int Rgb(Color color)
    {
        color.ToRgb(out var r, out var g, out var b);
        return (r << 16) | (g << 8) | b;
    }
}
