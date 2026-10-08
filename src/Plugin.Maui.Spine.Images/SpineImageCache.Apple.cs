#if IOS || MACCATALYST

using CoreGraphics;
using Foundation;
using ImageIO;
using Microsoft.Extensions.Logging;
using UIKit;

namespace Plugin.Maui.Spine.Images;

/// <summary>
/// iOS and Mac Catalyst: files in <c>Caches/spine-images</c>, decoded images in an <see cref="NSCache"/>
/// that UIKit empties under memory pressure, and decoding with ImageIO at the size the view needs, off the
/// main thread.
/// </summary>
internal sealed partial class SpineImageCache : IImageCache
{
    readonly NSCache _memory;
    readonly TimeSpan _prefetchValidity;

    SpineImageCache(SpineImagesOptions options, ILogger? logger)
    {
        Files = new(Path.Combine(FileSystem.CacheDirectory, "spine-images"), options, Http, logger: logger);
        _memory = new NSCache { Name = "Spine.Images", TotalCostLimit = (nuint)Math.Max(0, options.MemoryCacheSize) };
        _prefetchValidity = options.PrefetchValidity;
        MemoryEnabled = options.MemoryCacheSize > 0;
    }

    public ImageFileCache Files { get; }

    bool MemoryEnabled { get; }

    public UIImage? FromMemory(string key) =>
        MemoryEnabled ? _memory.ObjectForKey(new NSString(key)) as UIImage : null;

    public void ToMemory(string key, UIImage image)
    {
        if (!MemoryEnabled || image.CGImage is not { } cg)
            return;
        _memory.SetCost(image, new NSString(key), (nuint)(cg.BytesPerRow * cg.Height));
    }

    public Task PrefetchAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken = default) =>
        Files.PrefetchAsync(uris, _prefetchValidity, cancellationToken);

    public Task<bool> ContainsAsync(Uri uri, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.Contains(uri));

    public async Task<Stream> LoadPngAsync(Uri uri, int maxPixelSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPixelSize, 1);
        var file = await Files.GetAsync(uri, _prefetchValidity, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            using var source = Open(file, uri);
            var (width, height) = OrientedSize(source);
            using var cg = Thumbnail(source, Math.Min(maxPixelSize, Math.Max(width, height)), uri);
            using var image = new UIImage(cg);
            using var png = image.AsPNG() ?? throw new InvalidOperationException($"{uri} could not be written as PNG.");
            return (Stream)new MemoryStream(png.ToArray(), writable: false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(ImageCacheScope scope = ImageCacheScope.All)
    {
        if (scope.HasFlag(ImageCacheScope.Memory))
            _memory.RemoveAllObjects();
        if (scope.HasFlag(ImageCacheScope.Disk))
            await Files.ClearAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Decodes <paramref name="file"/> for <paramref name="target"/>, or <see langword="null"/> for an animated
    /// image, which MAUI's own service plays. The image measures as MAUI's would, one point per pixel of the
    /// original, however few pixels it was decoded with, so a page lays out the same with the package.
    /// </summary>
    public static UIImage? Decode(ImageFile file, ImageTarget target, Uri uri)
    {
        using var source = Open(file, uri);
        if (source.ImageCount > 1)
            return null;

        var (width, height) = OrientedSize(source);
        using var cg = Thumbnail(source, target.MaxPixelSize(width, height), uri);
        var scale = (double)Math.Max(cg.Width, cg.Height) / Math.Max(1, Math.Max(width, height));
        return new UIImage(cg, (nfloat)scale, UIImageOrientation.Up);
    }

    static CGImageSource Open(ImageFile file, Uri uri)
    {
        var source = file.Path is { } path
            ? CGImageSource.FromUrl(NSUrl.CreateFileUrl(path))
            : CGImageSource.FromData(NSData.FromArray(file.Bytes ?? []));
        if (source is null || source.ImageCount < 1)
        {
            source?.Dispose();
            Discard(file);
            throw new InvalidOperationException($"{uri} is not an image ImageIO can read.");
        }
        return source;
    }

    // A file that does not decode (an error page served as 200, a truncated body) would otherwise be shown
    // as broken for as long as it is fresh.
    static void Discard(ImageFile file)
    {
        if (file.Path is { } path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
        }
    }

    static (int Width, int Height) OrientedSize(CGImageSource source)
    {
        var properties = source.GetProperties(0, null);
        int width = properties?.PixelWidth ?? 0, height = properties?.PixelHeight ?? 0;
        // EXIF orientations 5–8 turn the picture a quarter, so its width is the stored height.
        return (int?)properties?.Orientation is >= 5 and <= 8 ? (height, width) : (width, height);
    }

    // ImageIO decodes straight to the size asked for, and ShouldCacheImmediately makes it do so here, on
    // this thread, rather than lazily on the main thread when the view first draws.
    static CGImage Thumbnail(CGImageSource source, int maxPixelSize, Uri uri) =>
        source.CreateThumbnail(0, new CGImageThumbnailOptions
        {
            MaxPixelSize = Math.Max(1, maxPixelSize),
            CreateThumbnailFromImageAlways = true,
            CreateThumbnailWithTransform = true,
            ShouldCacheImmediately = true,
        }) ?? throw new InvalidOperationException($"{uri} could not be decoded.");
}

#endif
