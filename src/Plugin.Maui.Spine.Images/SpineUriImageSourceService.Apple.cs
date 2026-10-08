#if IOS || MACCATALYST

using UIKit;

namespace Plugin.Maui.Spine.Images;

/// <summary>
/// Loads a <see cref="UriImageSource"/> through <see cref="SpineImageCache"/>: memory, then disk, then the
/// network, decoded for the view's box off the main thread. Animated images go to MAUI's stream service,
/// which plays them, from the same cached file.
/// </summary>
internal sealed class SpineUriImageSourceService : ImageSourceService, IImageSourceService<UriImageSource>
{
    // MAUI's image source factory creates services with a parameterless constructor.
    public SpineUriImageSourceService()
    {
    }

    public override Task<IImageSourceServiceResult<UIImage>?> GetImageAsync(IImageSource imageSource, float scale = 1, CancellationToken cancellationToken = default)
    {
        // Taken before anything else: MAUI calls this synchronously from the Source mapping that left it.
        var target = ImageTarget.TakePending();
        if (imageSource is not UriImageSource { IsEmpty: false } source)
            return Task.FromResult<IImageSourceServiceResult<UIImage>?>(null);

        var cache = SpineImageCache.Shared;
        var key = target.Key(source.Uri);

        // A hit completes synchronously, so MAUI sets the image in the same pass and a recycled cell never
        // shows its previous image or the placeholder.
        if (source.CachingEnabled && cache.FromMemory(key) is { } hit)
            return Task.FromResult<IImageSourceServiceResult<UIImage>?>(new ImageSourceServiceResult(hit));

        return LoadAsync(cache, source, target, key, cancellationToken);
    }

    // No ConfigureAwait(false): MAUI sets the returned image on a UIView from the thread this ends on.
    static async Task<IImageSourceServiceResult<UIImage>?> LoadAsync(SpineImageCache cache, UriImageSource source, ImageTarget target, string key, CancellationToken cancellationToken)
    {
        var uri = source.Uri;
        var file = await cache.Files.GetAsync(uri, source.CacheValidity, source.CachingEnabled, cancellationToken);
        var image = await Task.Run(() => SpineImageCache.Decode(file, target, uri), cancellationToken);
        if (image is null)
            return await new StreamImageSourceService().GetImageAsync(
                new StreamImageSource { Stream = _ => Task.FromResult(file.OpenRead()) }, 1, cancellationToken);

        if (source.CachingEnabled)
            cache.ToMemory(key, image);

        // No dispose action: MAUI disposes the previous result when the view loads its next image, and the
        // same UIImage may sit in the memory cache and in other views.
        return new ImageSourceServiceResult(image);
    }
}

#endif
