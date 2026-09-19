using SkiaSharp;
using Svg.Skia;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Draws an SVG to a PNG, scaled to fit and centred, optionally tinted. SkiaSharp and Svg.Skia only —
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

    /// <summary>Renders <paramref name="svg"/> into a <paramref name="width"/> × <paramref name="height"/> PNG.</summary>
    /// <param name="svg">The SVG document.</param>
    /// <param name="width">Bitmap width in pixels.</param>
    /// <param name="height">Bitmap height in pixels.</param>
    /// <param name="tint">The colour every drawn pixel takes; one with alpha 0 keeps the SVG's own colours.</param>
    /// <param name="left">Inset from the left edge, in pixels.</param>
    /// <param name="top">Inset from the top edge, in pixels.</param>
    /// <param name="right">Inset from the right edge, in pixels.</param>
    /// <param name="bottom">Inset from the bottom edge, in pixels.</param>
    /// <returns>The encoded PNG.</returns>
    public static byte[] RenderPng(Stream svg, int width, int height, SKColor tint, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        using var document = new SKSvg();
        document.Load(svg);
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

            using var paint = TintPaint(tint);
            canvas.DrawPicture(picture, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
