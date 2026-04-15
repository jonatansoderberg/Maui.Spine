using SkiaSharp;
using Svg.Skia;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Plugin.Maui.SvgImage;

/// <summary>
/// Provides static helpers for rendering embedded SVG resources into MAUI
/// <see cref="ImageSource"/> instances using SkiaSharp.
/// </summary>
/// <remarks>
/// Rendered bitmaps are memoised in an in-process cache keyed on resource name,
/// dimensions, tint colour, and padding, so repeated requests for the same
/// combination are free after the first render.
/// </remarks>
public static class SvgBitmapLoader
{
    /// <summary>
    /// Optional registry set by <see cref="MauiAppBuilderExtensions.UseEmbeddedSvgImages"/>.
    /// When set, stream resolution for embedded SVGs is delegated here first.
    /// </summary>
    internal static ResourceNameCache? Registry { get; set; }

    private static readonly ConcurrentDictionary<string, Lazy<ReadOnlyMemory<byte>>> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    
    private static string BuildImageCacheKey(string resourceName, double width, double height, Color tint, Thickness padding) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{resourceName}|{width:F2}x{height:F2}|{tint.Red:F3},{tint.Green:F3},{tint.Blue:F3},{tint.Alpha:F3}|{padding.Left:F2},{padding.Top:F2},{padding.Right:F2},{padding.Bottom:F2}");

    /// <summary>
    /// Loads an embedded SVG resource and returns a MAUI <see cref="ImageSource"/> rendered at
    /// the specified dimensions, tint colour, and padding.
    /// </summary>
    /// <param name="svgName">
    /// The fully-qualified manifest resource name of the SVG
    /// (e.g. <c>"MyApp.Images.icon.svg"</c>). Resolve short names first via
    /// <see cref="ResourceNameCache.Resolve"/>.
    /// </param>
    /// <param name="width">The target render width in pixels. Must be greater than zero.</param>
    /// <param name="height">The target render height in pixels. Must be greater than zero.</param>
    /// <param name="tint">
    /// A colour applied as a <c>SrcIn</c> blend over the SVG, allowing the icon to be
    /// recoloured at runtime. Pass <see cref="Colors.Transparent"/> to keep the original colours.
    /// </param>
    /// <param name="padding">
    /// Inset padding (in pixels) applied around the SVG within the canvas.
    /// The SVG is scaled to fit the remaining safe area and centred.
    /// </param>
    /// <returns>
    /// A stream-backed <see cref="ImageSource"/>, or <see langword="null"/> if
    /// <paramref name="svgName"/> is null or white-space.
    /// </returns>
    public static ImageSource? LoadFromEmbedded(string svgName, double width, double height, Color tint, Thickness padding)
    {
        if (string.IsNullOrWhiteSpace(svgName))
            return null;

        var key = BuildImageCacheKey(svgName, width, height, tint, padding);

        var lazyImage = _imageCache.GetOrAdd(key, _ =>
            new Lazy<ReadOnlyMemory<byte>>(
                () => RenderSvgToPng(svgName, width, height, tint, padding),
                LazyThreadSafetyMode.ExecutionAndPublication));

        var memory = lazyImage.Value;

        if (!MemoryMarshal.TryGetArray(memory, out var segment))
            throw new InvalidOperationException("Memory is not array-backed");

        return ImageSource.FromStream(() =>
            new MemoryStream(
                segment.Array!,
                segment.Offset,
                segment.Count,
                writable: false,
                publiclyVisible: true));
    }

    /// <summary>
    /// Loads an embedded SVG resource and returns a MAUI <see cref="ImageSource"/> rendered at
    /// the specified dimensions and tint colour, with no padding.
    /// </summary>
    /// <param name="svgName">
    /// The fully-qualified manifest resource name of the SVG
    /// (e.g. <c>"MyApp.Images.icon.svg"</c>). Resolve short names first via
    /// <see cref="ResourceNameCache.Resolve"/>.
    /// </param>
    /// <param name="width">The target render width in pixels. Must be greater than zero.</param>
    /// <param name="height">The target render height in pixels. Must be greater than zero.</param>
    /// <param name="tint">
    /// A colour applied as a <c>SrcIn</c> blend over the SVG, allowing the icon to be
    /// recoloured at runtime. Pass <see cref="Colors.Transparent"/> to keep the original colours.
    /// </param>
    /// <returns>
    /// A stream-backed <see cref="ImageSource"/>, or <see langword="null"/> if
    /// <paramref name="svgName"/> is null or white-space.
    /// </returns>
    public static ImageSource? LoadFromEmbedded(string svgName, double width, double height, Color tint)
        => LoadFromEmbedded(svgName, width, height, tint, new Thickness(0));

    private static ReadOnlyMemory<byte> RenderSvgToPng(string resourceName, double width, double height, Color tint, Thickness padding)
    {
        if (resourceName is null)
            throw new FileNotFoundException(resourceName);

        using var stream =
            Registry?.OpenStream(resourceName)
            ?? (Assembly.GetAssembly(typeof(SvgBitmapLoader)) ?? Assembly.GetExecutingAssembly())
               .GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(resourceName);

        using var svg = new SKSvg();
        svg.Load(stream);

        var picture = svg.Picture!;
        var info = new SKImageInfo((int)width, (int)height);

        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Transparent);

        // Safe zone inside the bitmap
        var safeWidth = Math.Max(0f, (float)width - (float)padding.Left - (float)padding.Right);
        var safeHeight = Math.Max(0f, (float)height - (float)padding.Top - (float)padding.Bottom);

        if (safeWidth <= 0 || safeHeight <= 0)
        {
            using var emptyImage = SKImage.FromBitmap(bitmap);
            using var emptyData = emptyImage.Encode(SKEncodedImageFormat.Png, 100);
            return emptyData.ToArray();
        }

        // Compute scale to fit while keeping aspect ratio (within safe zone)
        var scale = Math.Min(
            safeWidth / picture.CullRect.Width,
            safeHeight / picture.CullRect.Height);

        // Compute scaled size
        var scaledWidth = picture.CullRect.Width * scale;
        var scaledHeight = picture.CullRect.Height * scale;

        // Compute offsets to center the image within the safe zone
        var offsetX = (float)padding.Left + (safeWidth - scaledWidth) / 2f;
        var offsetY = (float)padding.Top + (safeHeight - scaledHeight) / 2f;

        // Translate to center, then scale to fit
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale);

        // Normalize for non-zero cull rect origin
        canvas.Translate(-picture.CullRect.Left, -picture.CullRect.Top);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            ColorFilter = SKColorFilter.CreateBlendMode(
                new SKColor(
                    (byte)(tint.Red * 255),
                    (byte)(tint.Green * 255),
                    (byte)(tint.Blue * 255),
                    (byte)(tint.Alpha * 255)),
                SKBlendMode.SrcIn)
        };

        canvas.DrawPicture(picture, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }
}
