using SkiaSharp;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Draws an embedded SVG straight onto an <see cref="SKCanvas"/>, for controls that paint with SkiaSharp themselves
/// (an icon inside an AnimatedLabel, a chart's legend). Images in XAML use <see cref="SvgImageSource"/> instead.
/// </summary>
public static class SvgDrawing
{
    /// <summary>
    /// Draws the embedded SVG named <paramref name="svgFileName"/> into <paramref name="bounds"/>, scaled to fit and
    /// centred, in <paramref name="tint"/>: its <c>currentColor</c> parts, or all of it when it has none.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="svgFileName">The short file name, <c>"Crown.svg"</c>, as in <see cref="SvgImageSource"/>.</param>
    /// <param name="bounds">Where, in the canvas's pixels.</param>
    /// <param name="tint">The colour; one with alpha 0 keeps the SVG's own colours.</param>
    /// <param name="lineWidthScale">Multiplies every stroke width; <c>1</c> keeps the SVG's own.</param>
    /// <param name="roundLineEnds">Ends every stroke in a half circle and rounds its corners.</param>
    /// <returns>False when there is no such SVG (or embedded SVGs are not registered, see <c>UseEmbeddedSvgImages</c>).</returns>
    public static bool Draw(SKCanvas canvas, string svgFileName, SKRect bounds, SKColor tint, float lineWidthScale = 1,
        bool roundLineEnds = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (string.IsNullOrWhiteSpace(svgFileName) || bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        using var stream = SvgBitmapLoader.Registry?.OpenStream(svgFileName);
        if (stream is null)
            return false;

        using var document = SvgRasterizer.Load(stream, tint, out var tintPaint, lineWidthScale: lineWidthScale, roundLineEnds: roundLineEnds);
        using var paint = tintPaint;
        if (document.Picture is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
            return false;

        var scale = Math.Min(bounds.Width / picture.CullRect.Width, bounds.Height / picture.CullRect.Height);
        canvas.Save();
        canvas.Translate(bounds.Left + (bounds.Width - picture.CullRect.Width * scale) / 2f,
            bounds.Top + (bounds.Height - picture.CullRect.Height * scale) / 2f);
        canvas.Scale(scale);
        canvas.Translate(-picture.CullRect.Left, -picture.CullRect.Top);
        canvas.DrawPicture(picture, paint);
        canvas.Restore();
        return true;
    }
}
