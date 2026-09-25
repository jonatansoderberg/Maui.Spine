using SkiaSharp;
using Svg.Skia;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Plugin.Maui.Spine.Svg;

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
    /// Optional registry set by <see cref="MauiAppBuilderExtensions.UseEmbeddedSvgImages(MauiAppBuilder)"/>.
    /// When set, stream resolution for embedded SVGs is delegated here first.
    /// </summary>
    internal static ResourceNameCache? Registry { get; set; }

    /// <summary>The options from <see cref="MauiAppBuilderExtensions.UseEmbeddedSvgImages(MauiAppBuilder, Action{SvgImageOptions}, Assembly[])"/>.</summary>
    internal static SvgImageOptions Options { get; } = new();

    private static SvgDarkPalette? _darkPalette;

    // Built on first use: the options are set in MauiProgram, before any image renders.
    internal static SvgDarkPalette DarkPalette => _darkPalette ??= Options.ToPalette();

    private static readonly ConcurrentDictionary<string, Lazy<ReadOnlyMemory<byte>>> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    
    private static string BuildImageCacheKey(string resourceName, double width, double height, Color tint, Thickness padding, bool adjustColorsForDark, float lineWidthScale) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{resourceName}|{width:F2}x{height:F2}|{tint.Red:F3},{tint.Green:F3},{tint.Blue:F3},{tint.Alpha:F3}|{padding.Left:F2},{padding.Top:F2},{padding.Right:F2},{padding.Bottom:F2}|{adjustColorsForDark}|{lineWidthScale:F3}");

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
    /// The colour of the SVG's <c>currentColor</c> parts, or of the whole SVG when it has none. A colour
    /// with no alpha, such as <see cref="Colors.Transparent"/>, keeps the original colours.
    /// </param>
    /// <param name="padding">
    /// Inset padding (in pixels) applied around the SVG within the canvas.
    /// The SVG is scaled to fit the remaining safe area and centred.
    /// </param>
    /// <param name="adjustColorsForDark">
    /// Gives the SVG's own colours their dark tones, from <see cref="SvgImageOptions.DarkColors"/> or the
    /// automatic rule; the tint is left as it is. Pass it only when the image is shown in the dark theme.
    /// </param>
    /// <param name="lineWidthScale">
    /// Multiplies every stroke width in the SVG: above <c>1</c> for thicker lines at small sizes, below
    /// for thinner ones at large sizes. <c>1</c> keeps the SVG's own.
    /// </param>
    /// <returns>
    /// A stream-backed <see cref="ImageSource"/>, or <see langword="null"/> if
    /// <paramref name="svgName"/> is null or white-space.
    /// </returns>
    public static ImageSource? LoadFromEmbedded(string svgName, double width, double height, Color tint, Thickness padding,
        bool adjustColorsForDark = false, float lineWidthScale = 1)
    {
        if (string.IsNullOrWhiteSpace(svgName))
            return null;

        // The size is in points; the bitmap is rendered for the screen it will be shown on, and the
        // image source carries the scale so the platform decodes it back to the same point size.
        var scale = ScreenScale;
        var pixelWidth = width * scale;
        var pixelHeight = height * scale;
        var pixelPadding = new Thickness(padding.Left * scale, padding.Top * scale, padding.Right * scale, padding.Bottom * scale);

        var key = BuildImageCacheKey(svgName, pixelWidth, pixelHeight, tint, pixelPadding, adjustColorsForDark, lineWidthScale);

        var lazyImage = _imageCache.GetOrAdd(key, _ =>
            new Lazy<ReadOnlyMemory<byte>>(
                () => RenderSvgToPng(svgName, pixelWidth, pixelHeight, tint, pixelPadding, adjustColorsForDark, lineWidthScale),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return new SvgBitmapImageSource(svgName, lazyImage.Value, scale);
    }

    // A WinUI BitmapImage decoded from a stream shows its own pixels, so Windows stays at 1× until
    // DecodePixelType.Logical can be verified there.
    private static float ScreenScale =>
        OperatingSystem.IsWindows() ? 1f : (float)DeviceDisplay.MainDisplayInfo.Density;

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
    /// The colour of the SVG's <c>currentColor</c> parts, or of the whole SVG when it has none. A colour
    /// with no alpha, such as <see cref="Colors.Transparent"/>, keeps the original colours.
    /// </param>
    /// <returns>
    /// A stream-backed <see cref="ImageSource"/>, or <see langword="null"/> if
    /// <paramref name="svgName"/> is null or white-space.
    /// </returns>
    public static ImageSource? LoadFromEmbedded(string svgName, double width, double height, Color tint)
        => LoadFromEmbedded(svgName, width, height, tint, new Thickness(0));

    /// <summary>
    /// Loads Svg.Skia and SkiaSharp and runs one rasterization, so the first real icon does not pay
    /// for that on the main thread. Meant to run on a background thread at startup.
    /// </summary>
    internal static void WarmUp()
    {
        const string dot = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 2 2\"><circle cx=\"1\" cy=\"1\" r=\"1\"/></svg>";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(dot));
        using var svg = new SKSvg();
        svg.Load(stream);

        using var bitmap = new SKBitmap(new SKImageInfo(2, 2));
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawPicture(svg.Picture);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    }

    private static ReadOnlyMemory<byte> RenderSvgToPng(string resourceName, double width, double height, Color tint, Thickness padding,
        bool adjustColorsForDark, float lineWidthScale)
    {
        if (resourceName is null)
            throw new FileNotFoundException(resourceName);

        using var stream =
            Registry?.OpenStream(resourceName)
            ?? (Assembly.GetAssembly(typeof(SvgBitmapLoader)) ?? Assembly.GetExecutingAssembly())
               .GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(resourceName);

        return SvgRasterizer.RenderPng(stream, (int)Math.Round(width), (int)Math.Round(height),
            new SKColor((byte)(tint.Red * 255), (byte)(tint.Green * 255), (byte)(tint.Blue * 255), (byte)(tint.Alpha * 255)),
            adjustColorsForDark ? DarkPalette : null, lineWidthScale,
            (float)padding.Left, (float)padding.Top, (float)padding.Right, (float)padding.Bottom);
    }
}
