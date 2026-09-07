using Plugin.Maui.SvgImage;
using SkiaSharp;
using Svg.Skia;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Turns every <see cref="IconNode"/> in a tree into a bitmap the renderers can show. Icons come
/// from SVGs in the resource cache Spine fills — the ones bundled with <c>Plugin.Maui.SvgImage</c>
/// and any the app embeds — named after the symbol with dots as underscores. The shape is rendered
/// white, so the renderer tints it with the node's color in light and dark alike, and stored as
/// <c>icons/&lt;name&gt;.png</c> beside the app's own assets before the tree is written.
/// </summary>
internal sealed class WidgetIconAssets(IWidgetPlatform _platform, ResourceNameCache _svgs)
{
    /// <summary>Rendered size in pixels; large enough for a 3x display at the sizes widgets draw icons.</summary>
    private const int Pixels = 128;

    private readonly HashSet<string> _stored = [];

    public static string AssetId(string name) => "icons/" + name + ".png";

    public async Task EnsureAsync(IEnumerable<WidgetNode?> trees, CancellationToken cancellationToken)
    {
        if (!_platform.IsSupported) return;

        foreach (var name in trees.SelectMany(Icons).Distinct(StringComparer.Ordinal))
        {
            lock (_stored) if (!_stored.Add(name)) continue;

            using var stream = Open(name);
            if (stream is null || Render(stream) is not { } png)
            {
                lock (_stored) _stored.Remove(name);
                continue;
            }

            await _platform.StoreAssetAsync(AssetId(name), new MemoryStream(png), cancellationToken);
        }
    }

    public Task EnsureAsync(LiveActivityLayout layout, CancellationToken cancellationToken) =>
        EnsureAsync([layout.LockScreen, layout.ExpandedLeading, layout.ExpandedTrailing, layout.ExpandedCenter,
            layout.ExpandedBottom, layout.CompactLeading, layout.CompactTrailing, layout.Minimal], cancellationToken);

    private static IEnumerable<string> Icons(WidgetNode? node) => node switch
    {
        IconNode icon when !string.IsNullOrWhiteSpace(icon.SystemName) => [icon.SystemName],
        StackNode stack => stack.Children.SelectMany(Icons),
        _ => [],
    };

    // The underscore form first: the SDK reads "figure.run.svg" as a resource for the culture "run".
    private Stream? Open(string name) =>
        _svgs.OpenStream(name.Replace('.', '_') + ".svg") ?? _svgs.OpenStream(name + ".svg");

    private static byte[]? Render(Stream svgStream)
    {
        using var svg = new SKSvg();
        svg.Load(svgStream);
        if (svg.Picture is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0) return null;

        using var bitmap = new SKBitmap(Pixels, Pixels, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var bounds = picture.CullRect;
        var scale = Math.Min(Pixels / bounds.Width, Pixels / bounds.Height);
        canvas.Translate((Pixels - bounds.Width * scale) / 2, (Pixels - bounds.Height * scale) / 2);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);

        using var paint = new SKPaint { IsAntialias = true, ColorFilter = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.SrcIn) };
        canvas.DrawPicture(picture, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
