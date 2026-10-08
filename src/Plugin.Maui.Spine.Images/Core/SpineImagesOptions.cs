namespace Plugin.Maui.Spine.Images;

/// <summary>App-wide settings for <c>Plugin.Maui.Spine.Images</c>, set with <c>UseSpineImages(o => …)</c>.</summary>
public sealed class SpineImagesOptions
{
    /// <summary>
    /// The most the disk cache may hold, in bytes; past it the least recently shown images are deleted
    /// down to three quarters. iOS, Mac Catalyst and Windows; on Android Glide's own limit (250 MB) applies.
    /// Default 150 MB.
    /// </summary>
    public long DiskCacheSize { get; set; } = 150L * 1024 * 1024;

    /// <summary>
    /// The most the memory cache of decoded images may hold, in bytes. iOS and Mac Catalyst, where it is an
    /// <c>NSCache</c> that also empties itself under memory pressure; on Android Glide's memory cache applies.
    /// Default 100 MB; 0 turns the memory cache off.
    /// </summary>
    public long MemoryCacheSize { get; set; } = 100L * 1024 * 1024;

    /// <summary>How many images download at the same time. Default 4.</summary>
    public int MaxConcurrentDownloads { get; set; } = 4;

    /// <summary>
    /// How long a prefetched image counts as fresh, as <c>UriImageSource.CacheValidity</c> does for an image
    /// a view loads. Default one day, the same as MAUI's default.
    /// </summary>
    public TimeSpan PrefetchValidity { get; set; } = TimeSpan.FromDays(1);
}
