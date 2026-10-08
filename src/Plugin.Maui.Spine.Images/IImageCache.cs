namespace Plugin.Maui.Spine.Images;

/// <summary>
/// The app's cache of remote images. Every <see cref="UriImageSource"/> an <see cref="Image"/> shows goes
/// through it once the package is registered; inject it to prefetch, check, clear or hand an image on.
/// </summary>
/// <remarks>
/// iOS, Mac Catalyst and Windows keep Spine's own disk cache (and on Apple a memory cache of decoded
/// images); Android keeps the Glide cache MAUI already loads through, so a prefetched image is the one a
/// view finds there.
/// </remarks>
public interface IImageCache
{
    /// <summary>
    /// Downloads the images into the disk cache, so they show later without waiting for the network, or
    /// without it at all. Images already there and fresh are skipped.
    /// </summary>
    /// <exception cref="HttpRequestException">An image could not be downloaded.</exception>
    /// <exception cref="AggregateException">More than one image could not be downloaded.</exception>
    Task PrefetchAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="uri"/> is in the disk cache. Nothing is downloaded.</summary>
    Task<bool> ContainsAsync(Uri uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// The image at most <paramref name="maxPixelSize"/> pixels on its longest side, as PNG, from the cache or
    /// the network. For handing a picture to something outside the app's views, such as a widget through
    /// <c>IWidgetService.StoreAssetAsync</c>.
    /// </summary>
    Task<Stream> LoadPngAsync(Uri uri, int maxPixelSize, CancellationToken cancellationToken = default);

    /// <summary>Empties the memory cache, the disk cache or both.</summary>
    Task ClearAsync(ImageCacheScope scope = ImageCacheScope.All);
}
