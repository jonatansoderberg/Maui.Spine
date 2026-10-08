#if WINDOWS

using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Plugin.Maui.Spine.Images;

/// <summary>
/// Windows: MAUI caches nothing here and opens a new HttpClient per image; the package keeps the files in
/// <c>spine-images</c> under the app's cache directory and decodes them at the view's size. Decoded images
/// are not kept in memory: WinUI's own image cache holds what is on screen.
/// </summary>
internal sealed partial class SpineImageCache : IImageCache
{
    readonly TimeSpan _prefetchValidity;

    SpineImageCache(SpineImagesOptions options, ILogger? logger)
    {
        Files = new(Path.Combine(FileSystem.CacheDirectory, "spine-images"), options, Http, logger: logger);
        _prefetchValidity = options.PrefetchValidity;
    }

    public ImageFileCache Files { get; }

    public Task PrefetchAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken = default) =>
        Files.PrefetchAsync(uris, _prefetchValidity, cancellationToken);

    public Task<bool> ContainsAsync(Uri uri, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.Contains(uri));

    public async Task<Stream> LoadPngAsync(Uri uri, int maxPixelSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPixelSize, 1);
        var file = await Files.GetAsync(uri, _prefetchValidity, cancellationToken: cancellationToken).ConfigureAwait(false);

        using var input = file.OpenRead().AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(input);
        var longest = Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
        var scale = Math.Min(1, (double)maxPixelSize / Math.Max(1, longest));

        var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateForTranscodingAsync(output, decoder);
        encoder.BitmapTransform.ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
        encoder.BitmapTransform.ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        await encoder.FlushAsync();

        var png = new MemoryStream();
        output.Seek(0);
        await output.AsStreamForRead().CopyToAsync(png, cancellationToken).ConfigureAwait(false);
        png.Position = 0;
        return png;
    }

    public async Task ClearAsync(ImageCacheScope scope = ImageCacheScope.All)
    {
        if (scope.HasFlag(ImageCacheScope.Disk))
            await Files.ClearAsync().ConfigureAwait(false);
    }
}

#endif
