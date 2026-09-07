using System.Collections.Concurrent;
using Android.Graphics;
using Plugin.Maui.SvgImage;
using SkiaSharp;
using Svg.Skia;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Android has no SF Symbols, so an <see cref="IconNode"/> is drawn from an embedded SVG named after
/// the symbol with dots as underscores (<c>figure_run.svg</c>) in the assemblies <c>UseSpine</c> was given.
/// The dotted name is accepted too, but the SDK reads <c>figure.run.svg</c> as a resource for the culture
/// "run" (Kirundi) and moves it to a satellite assembly, so the underscore form is the one to document.
/// The shape is rendered white and tinted by the view, so one bitmap serves light and dark.
/// </summary>
internal sealed class WidgetIcons(ResourceNameCache _svgs)
{
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new();

    public Bitmap? Bitmap(string? name, int pixels)
    {
        if (string.IsNullOrWhiteSpace(name) || pixels <= 0) return null;
        return _cache.GetOrAdd($"{name}@{pixels}", _ => Render(name, pixels));
    }

    private Bitmap? Render(string name, int pixels)
    {
        using var stream = _svgs.OpenStream(name.Replace('.', '_') + ".svg") ?? _svgs.OpenStream(name + ".svg");
        if (stream is null)
        {
            Android.Util.Log.Warn("SpineWidgets", $"No embedded SVG for icon \"{name}\"; expected {name.Replace('.', '_')}.svg.");
            return null;
        }

        using var svg = new SKSvg();
        svg.Load(stream);
        if (svg.Picture is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0) return null;

        using var surface = new SKBitmap(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(surface);
        canvas.Clear(SKColors.Transparent);

        var bounds = picture.CullRect;
        var scale = Math.Min(pixels / bounds.Width, pixels / bounds.Height);
        canvas.Translate((pixels - bounds.Width * scale) / 2, (pixels - bounds.Height * scale) / 2);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);

        using var paint = new SKPaint { IsAntialias = true, ColorFilter = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.SrcIn) };
        canvas.DrawPicture(picture, paint);

        using var image = SKImage.FromBitmap(surface);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = png.ToArray();
        return BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
    }
}
