using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Images;

internal sealed partial class SpineImageCache
{
    static SpineImageCache? _shared;

    /// <summary>The app's cache, for the image source service, which MAUI creates outside the app's container.</summary>
    public static SpineImageCache Shared => _shared ??= IPlatformApplication.Current?.Services.GetService<IImageCache>() as SpineImageCache
        ?? throw new InvalidOperationException("Plugin.Maui.Spine.Images is not registered: call UseSpine() or UseSpineImages() in MauiProgram.");

    public static SpineImageCache Create(SpineImagesOptions options, ILogger? logger) => new(options, logger);

    // One client for every download: MAUI's own UriImageSource creates one per image.
    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
}
