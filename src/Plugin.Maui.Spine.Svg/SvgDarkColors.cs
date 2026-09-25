using Svg;
using DrawingColor = System.Drawing.Color;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// The dark tones for an SVG's own colours: exact pairs for the colours an app wants to choose itself,
/// and the automatic rule, muted by <paramref name="Muting"/>, for the rest.
/// </summary>
/// <param name="Map">Dark colour by light colour, both as <c>0xRRGGBB</c>; alpha is kept from the SVG.</param>
/// <param name="Muting">How much chroma the automatic rule takes away: 0 keeps it, 1 leaves grey.</param>
internal sealed record SvgDarkPalette(IReadOnlyDictionary<int, int> Map, double Muting)
{
    /// <summary>No pairs, and the default muting.</summary>
    public static SvgDarkPalette Default { get; } = new(new Dictionary<int, int>(), 0.15);
}

/// <summary>
/// Adjusts an SVG drawn for a light background so it fits a dark one. A colour in the palette's map
/// takes its pair. For any other, in CIELAB with the hue kept: a dark neutral gets the mirrored
/// lightness, 100 − L*, so black ink becomes white; a dark colour is lifted to a mid lightness, so navy
/// becomes a clear blue rather than a pastel; and every colour loses some chroma, so red turns a little
/// more matte. Nothing comes out darker than L* 50, about 3.8:1 against black.
/// </summary>
internal static class SvgDarkColors
{
    private static readonly string[] PaintAttributes = ["fill", "stroke", "color"];

    /// <summary>
    /// Adjusts every colour <paramref name="document"/> sets itself: <c>fill</c>, <c>stroke</c>,
    /// <c>color</c> and gradient <c>stop-color</c>, from attributes and styles. <c>currentColor</c>,
    /// gradients and <c>none</c> are left alone, and inherited values follow their ancestor.
    /// </summary>
    /// <param name="document">The SVG.</param>
    /// <param name="palette">The dark tones.</param>
    /// <param name="rootColorIsTint">
    /// Whether the root's <c>color</c> is the tint, which stays as it is. Set it before calling: reading
    /// a <c>currentColor</c> paint resolves it there and then, so it must already be final.
    /// </param>
    public static void Apply(SvgDocument document, SvgDarkPalette palette, bool rootColorIsTint)
    {
        // Unset, fill and currentColor are black; set them on the root so the adjustment reaches them.
        if (!document.ContainsAttribute("fill"))
            document.Fill = new SvgColourServer(DrawingColor.Black);
        if (!rootColorIsTint && !document.ContainsAttribute("color"))
            document.Color = new SvgColourServer(DrawingColor.Black);

        // Ancestors come before their descendants, so an adjusted color is in place before a child's
        // currentColor resolves against it.
        foreach (var element in document.Descendants().Prepend(document))
        {
            foreach (var attribute in PaintAttributes)
            {
                if (!element.ContainsAttribute(attribute) || (rootColorIsTint && element == document && attribute == "color"))
                    continue;

                switch (attribute)
                {
                    case "fill" when Adjusted(element.Fill, palette) is { } fill:
                        element.Fill = fill;
                        break;
                    case "stroke" when Adjusted(element.Stroke, palette) is { } stroke:
                        element.Stroke = stroke;
                        break;
                    case "color" when Adjusted(element.Color, palette) is { } color:
                        element.Color = color;
                        break;
                }
            }

            if (element is SvgGradientStop stop && element.ContainsAttribute("stop-color") && Adjusted(stop.StopColor, palette) is { } stopColor)
                stop.StopColor = stopColor;
        }
    }

    private static SvgColourServer? Adjusted(SvgPaintServer? server, SvgDarkPalette palette) =>
        server is SvgColourServer colour
        && !ReferenceEquals(server, SvgPaintServer.None)
        && !ReferenceEquals(server, SvgPaintServer.NotSet)
        && !ReferenceEquals(server, SvgPaintServer.Inherit)
            ? new SvgColourServer(ForDark(colour.Colour, palette))
            : null;

    /// <summary>The pair from the palette's map, or else the colour lightened and muted for a dark background.</summary>
    public static DrawingColor ForDark(DrawingColor colour, SvgDarkPalette palette)
    {
        if (palette.Map.TryGetValue(colour.ToArgb() & 0xFFFFFF, out var pair))
            return DrawingColor.FromArgb(colour.A, DrawingColor.FromArgb(pair));

        var (l, a, b) = ToLab(colour);

        // Neutrals flip and colours lift; chroma 40 and up counts as fully coloured.
        var coloured = Math.Clamp(Math.Sqrt(a * a + b * b) / 40, 0, 1);
        var flipped = l < 50 ? 100 - l : l;
        var lifted = Math.Max(l, 55);
        l = flipped + (lifted - flipped) * coloured;

        var keep = 1 - Math.Clamp(palette.Muting, 0, 1);
        a *= keep;
        b *= keep;

        // The mirrored lightness can leave sRGB for saturated colours (pure blue); keep the hue and
        // lightness and give up the least chroma that brings it back in.
        double low = 0, high = 1;
        if (!InGamut(l, a, b))
        {
            for (var i = 0; i < 20; i++)
            {
                var mid = (low + high) / 2;
                if (InGamut(l, a * mid, b * mid))
                    low = mid;
                else
                    high = mid;
            }
            a *= low;
            b *= low;
        }

        var (r, g, bl) = ToRgb(l, a, b);
        return DrawingColor.FromArgb(colour.A, ToByte(r), ToByte(g), ToByte(bl));
    }

    private static bool InGamut(double l, double a, double b)
    {
        var (r, g, bl) = ToRgb(l, a, b);
        return r is >= -1e-4 and <= 1 + 1e-4 && g is >= -1e-4 and <= 1 + 1e-4 && bl is >= -1e-4 and <= 1 + 1e-4;
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    // sRGB <-> CIELAB, D65 white.
    private const double Xn = 0.95047, Yn = 1.0, Zn = 1.08883;

    private static (double L, double A, double B) ToLab(DrawingColor c)
    {
        double r = Linear(c.R / 255.0), g = Linear(c.G / 255.0), b = Linear(c.B / 255.0);
        var x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / Xn;
        var y = (0.2126 * r + 0.7152 * g + 0.0722 * b) / Yn;
        var z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / Zn;
        double fx = F(x), fy = F(y), fz = F(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static (double R, double G, double B) ToRgb(double l, double a, double b)
    {
        var fy = (l + 16) / 116;
        var x = FInverse(fy + a / 500) * Xn;
        var y = FInverse(fy) * Yn;
        var z = FInverse(fy - b / 200) * Zn;
        return (
            Gamma(3.2406 * x - 1.5372 * y - 0.4986 * z),
            Gamma(-0.9689 * x + 1.8758 * y + 0.0415 * z),
            Gamma(0.0557 * x - 0.2040 * y + 1.0570 * z));
    }

    private static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double Gamma(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(Math.Max(v, 0), 1 / 2.4) - 0.055;

    private static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;

    private static double FInverse(double t) => t * t * t > 216.0 / 24389 ? t * t * t : (116 * t - 16) * 27 / 24389;
}
