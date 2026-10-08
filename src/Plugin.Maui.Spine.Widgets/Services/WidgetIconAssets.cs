using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Svg;
using SkiaSharp;
using Svg.Skia;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Turns every <see cref="IconNode"/> in a tree into a bitmap the renderers can show. Icons come
/// from SVGs in the resource cache Spine fills — the ones bundled with <c>Plugin.Maui.Spine.Svg</c>
/// and any the app embeds — named after the symbol with dots as underscores. The shape is rendered
/// white, so the renderer tints it with the node's color in light and dark alike, and stored as
/// <c>icons/&lt;name&gt;.png</c> beside the app's own assets before the tree is written.
/// </summary>
internal sealed class WidgetIconAssets(IWidgetPlatform _platform, ResourceNameCache _svgs)
{
    /// <summary>Rendered size in pixels; large enough for a 3x display at the sizes widgets draw icons.</summary>
    private const int Pixels = 128;

    private readonly HashSet<string> _stored = [];
    private readonly SemaphoreSlim _storing = new(1, 1);

    public static string AssetId(string name) => "icons/" + name + ".png";

    public Task EnsureAsync(IEnumerable<WidgetNode?> trees, CancellationToken cancellationToken) =>
        EnsureAsync(trees.SelectMany(WidgetTree.Icons), cancellationToken);

    /// <summary>Icons by name: a control's, which Android draws on its Quick Settings tile.</summary>
    public async Task EnsureAsync(IEnumerable<string> names, CancellationToken cancellationToken)
    {
        if (!_platform.IsSupported && !_platform.AreControlsSupported) return;

        // A refresh running beside this one must not write its tree before the icons it shares are on disk,
        // so it waits here rather than skipping a name another caller is still storing.
        await _storing.WaitAsync(cancellationToken);
        try
        {
            foreach (var name in names.Distinct(StringComparer.Ordinal))
            {
                if (_stored.Contains(name)) continue;

                using var stream = Open(name);
                if (stream is null || Render(stream) is not { } png) continue;

                await _platform.StoreAssetAsync(AssetId(name), new MemoryStream(png), cancellationToken);
                _stored.Add(name);
            }
        }
        finally
        {
            _storing.Release();
        }
    }

    public Task EnsureAsync(LiveActivityLayout layout, CancellationToken cancellationToken) =>
        EnsureAsync([layout.LockScreen, layout.ExpandedLeading, layout.ExpandedTrailing, layout.ExpandedCenter,
            layout.ExpandedBottom, layout.CompactLeading, layout.CompactTrailing, layout.Minimal], cancellationToken);

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
