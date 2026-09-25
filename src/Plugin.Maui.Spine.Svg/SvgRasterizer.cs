using System.Text;
using SkiaSharp;
using Svg;
using Svg.Skia;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Draws an SVG to a PNG, scaled to fit and centred, optionally tinted. An SVG that paints with
/// <c>currentColor</c> takes the tint only there and keeps its other colours; any other SVG is tinted whole. SkiaSharp and Svg.Skia only —
/// no MAUI types — so the test project can compile it on plain <c>net10.0</c>.
/// </summary>
internal static class SvgRasterizer
{
    /// <summary>
    /// The paint that recolours every drawn pixel to <paramref name="tint"/>, or <see langword="null"/>
    /// for a tint with no alpha: a <c>SrcIn</c> blend with a transparent colour would erase the
    /// picture, and "transparent" means "keep the SVG's own colours".
    /// </summary>
    public static SKPaint? TintPaint(SKColor tint) => tint.Alpha == 0
        ? null
        : new SKPaint
        {
            IsAntialias = true,
            ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn),
        };

    /// <summary>
    /// Parses <paramref name="svg"/> for drawing with <paramref name="tint"/>. When the SVG paints with
    /// <c>currentColor</c>, the tint becomes that colour, so only those parts change and the rest keep
    /// the SVG's own colours, and <paramref name="paint"/> is <see langword="null"/>. Any other SVG is
    /// recoloured whole by <paramref name="paint"/>, from <see cref="TintPaint"/>.
    /// </summary>
    /// <param name="svg">The SVG document.</param>
    /// <param name="tint">The tint; one with alpha 0 keeps the SVG's own colours.</param>
    /// <param name="paint">The paint to draw the picture with, or <see langword="null"/>.</param>
    /// <param name="darkPalette">
    /// Gives the SVG's own colours their dark tones (<see cref="SvgDarkColors"/>), or <see langword="null"/>
    /// to keep them. The tint is left as it is, and an SVG tinted whole has no own colours left to adjust.
    /// </param>
    /// <param name="lineWidthScale">Multiplies every stroke width; <c>1</c> keeps the SVG's own.</param>
    public static SKSvg Load(Stream svg, SKColor tint, out SKPaint? paint, SvgDarkPalette? darkPalette = null, float lineWidthScale = 1)
    {
        var document = new SKSvg();

        using var buffer = new MemoryStream();
        svg.CopyTo(buffer);
        buffer.Position = 0;

        var tintCurrentColor = tint.Alpha > 0 && Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length)
            .Contains("currentColor", StringComparison.OrdinalIgnoreCase);
        paint = tintCurrentColor ? null : TintPaint(tint);
        var scaleStrokes = Math.Abs(lineWidthScale - 1) > 1e-6f;
        if (paint is not null)
            darkPalette = null;

        if (!tintCurrentColor && darkPalette is null && !scaleStrokes)
        {
            document.Load(buffer);
            return document;
        }

        var model = SvgDocument.Open<SvgDocument>(buffer);

        if (scaleStrokes)
            ScaleStrokes(model, lineWidthScale);

        if (tintCurrentColor)
            model.Color = new SvgColourServer(System.Drawing.Color.FromArgb(tint.Alpha, tint.Red, tint.Green, tint.Blue));

        if (darkPalette is not null)
            SvgDarkColors.Apply(model, darkPalette, rootColorIsTint: tintCurrentColor);

        document.FromSvgDocument(model);
        return document;
    }

    /// <summary>
    /// Multiplies every stroke width the SVG sets, from attributes and styles, and the default of 1 on
    /// the root when it sets none there; inherited widths follow their ancestor.
    /// </summary>
    private static void ScaleStrokes(SvgDocument document, float scale)
    {
        foreach (var element in document.Descendants().Prepend(document))
        {
            if (element.ContainsAttribute("stroke-width"))
                element.StrokeWidth = new SvgUnit(element.StrokeWidth.Type, element.StrokeWidth.Value * scale);
            else if (element == document)
                document.StrokeWidth = new SvgUnit(scale);
        }
    }

    /// <summary>Renders <paramref name="svg"/> into a <paramref name="width"/> × <paramref name="height"/> PNG.</summary>
    /// <param name="svg">The SVG document.</param>
    /// <param name="width">Bitmap width in pixels.</param>
    /// <param name="height">Bitmap height in pixels.</param>
    /// <param name="tint">
    /// The colour of the SVG's <c>currentColor</c> parts, or of every drawn pixel when it has none; one with
    /// alpha 0 keeps the SVG's own colours.
    /// </param>
    /// <param name="darkPalette">Gives the SVG's own colours their dark tones, or <see langword="null"/> to keep them.</param>
    /// <param name="lineWidthScale">Multiplies every stroke width; <c>1</c> keeps the SVG's own.</param>
    /// <param name="left">Inset from the left edge, in pixels.</param>
    /// <param name="top">Inset from the top edge, in pixels.</param>
    /// <param name="right">Inset from the right edge, in pixels.</param>
    /// <param name="bottom">Inset from the bottom edge, in pixels.</param>
    /// <returns>The encoded PNG.</returns>
    public static byte[] RenderPng(Stream svg, int width, int height, SKColor tint, SvgDarkPalette? darkPalette = null, float lineWidthScale = 1, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        using var document = Load(svg, tint, out var tintPaint, darkPalette, lineWidthScale);
        using var paint = tintPaint;
        var picture = document.Picture ?? throw new InvalidOperationException("The SVG could not be parsed.");

        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Safe zone inside the bitmap
        var safeWidth = Math.Max(0f, width - left - right);
        var safeHeight = Math.Max(0f, height - top - bottom);

        if (safeWidth > 0 && safeHeight > 0)
        {
            // Scale to fit, keeping the aspect ratio, centred in the safe zone
            var scale = Math.Min(safeWidth / picture.CullRect.Width, safeHeight / picture.CullRect.Height);
            canvas.Translate(left + (safeWidth - picture.CullRect.Width * scale) / 2f, top + (safeHeight - picture.CullRect.Height * scale) / 2f);
            canvas.Scale(scale);

            // Normalize for non-zero cull rect origin
            canvas.Translate(-picture.CullRect.Left, -picture.CullRect.Top);

            canvas.DrawPicture(picture, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
