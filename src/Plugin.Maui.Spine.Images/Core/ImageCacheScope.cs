namespace Plugin.Maui.Spine.Images;

/// <summary>Which of the image caches <see cref="IImageCache.ClearAsync"/> empties.</summary>
[Flags]
public enum ImageCacheScope
{
    /// <summary>Decoded images in memory.</summary>
    Memory = 1,

    /// <summary>Downloaded files on disk.</summary>
    Disk = 2,

    /// <summary>Both.</summary>
    All = Memory | Disk,
}
