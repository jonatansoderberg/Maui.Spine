#if ANDROID

using Android.Graphics;
using Bumptech.Glide;
using Bumptech.Glide.Load.Resource.Bitmap;
using Java.Util.Concurrent;
using Microsoft.Extensions.Logging;
using AUri = Android.Net.Uri;

namespace Plugin.Maui.Spine.Images;

/// <summary>
/// Android: MAUI already loads every <see cref="UriImageSource"/> with Glide, which caches in memory and on
/// disk and decodes at the view's size, so the cache is Glide's. A prefetch asks Glide for the same model
/// MAUI does (an <see cref="AUri"/> of the source's original string), so the view finds the file.
/// </summary>
internal sealed partial class SpineImageCache : IImageCache
{
    readonly SemaphoreSlim _downloads;
    readonly ILogger? _logger;

    SpineImageCache(SpineImagesOptions options, ILogger? logger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrentDownloads, 1);
        _downloads = new(options.MaxConcurrentDownloads);
        _logger = logger;
    }

    static Android.Content.Context Context => Platform.AppContext;

    static AUri Model(Uri uri) => AUri.Parse(uri.OriginalString)
        ?? throw new ArgumentException($"{uri} is not a URI Android can parse.", nameof(uri));

    public async Task PrefetchAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken = default)
    {
        var tasks = uris.Distinct().Select(uri => DownloadAsync(uri, onlyFromCache: false, cancellationToken)).ToArray();
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch when (tasks.Count(static t => t.IsFaulted) > 1)
        {
            throw new AggregateException(
                $"{tasks.Count(static t => t.IsFaulted)} of {tasks.Length} images could not be prefetched.",
                tasks.Where(static t => t.IsFaulted).SelectMany(static t => t.Exception!.InnerExceptions));
        }
    }

    public async Task<bool> ContainsAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        try
        {
            await DownloadAsync(uri, onlyFromCache: true, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<Stream> LoadPngAsync(Uri uri, int maxPixelSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPixelSize, 1);
        var future = Glide.With(Context).AsBitmap().Load(Model(uri))
            .Downsample(DownsampleStrategy.CenterInside!)
            .Override(maxPixelSize)
            .Submit();
        var bitmap = await GetAsync(future, uri, cancellationToken).ConfigureAwait(false) as Bitmap
            ?? throw new InvalidOperationException($"Glide returned no bitmap for {uri}.");

        var png = new MemoryStream();
        if (!await bitmap.CompressAsync(Bitmap.CompressFormat.Png!, 100, png).ConfigureAwait(false))
            throw new InvalidOperationException($"{uri} could not be written as PNG.");
        png.Position = 0;
        return png;
    }

    public async Task ClearAsync(ImageCacheScope scope = ImageCacheScope.All)
    {
        if (scope.HasFlag(ImageCacheScope.Memory))
            await MainThread.InvokeOnMainThreadAsync(static () => Glide.Get(Context).ClearMemory()).ConfigureAwait(false);
        if (scope.HasFlag(ImageCacheScope.Disk))
            await Task.Run(static () => Glide.Get(Context).ClearDiskCache()).ConfigureAwait(false);
    }

    // downloadOnly() keeps the original bytes in Glide's disk cache (DATA), from which a view's load at its
    // own size decodes.
    async Task DownloadAsync(Uri uri, bool onlyFromCache, CancellationToken cancellationToken)
    {
        await _downloads.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var future = Glide.With(Context).DownloadOnly().Load(Model(uri)).SetOnlyRetrieveFromCache(onlyFromCache).Submit();
            await GetAsync(future, uri, cancellationToken).ConfigureAwait(false);
            if (!onlyFromCache)
                _logger?.LogDebug("Prefetched {Uri} into Glide's cache.", uri);
        }
        finally
        {
            _downloads.Release();
        }
    }

    static async Task<Java.Lang.Object?> GetAsync(Bumptech.Glide.Request.IFutureTarget future, Uri uri, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => future.Cancel(true));
        try
        {
            // The future blocks until Glide is done; never on the main thread.
            return await Task.Run(() => future.Get(), cancellationToken).ConfigureAwait(false);
        }
        catch (ExecutionException exception)
        {
            throw new HttpRequestException($"Glide could not load {uri}: {exception.Cause?.Message ?? exception.Message}", exception);
        }
        catch (CancellationException)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}

#endif
