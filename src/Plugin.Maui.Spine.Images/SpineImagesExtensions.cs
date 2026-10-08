using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Images;

/// <summary>Registers <c>Plugin.Maui.Spine.Images</c> with a <see cref="MauiAppBuilder"/>.</summary>
public static class SpineImagesExtensions
{
    static readonly SpineImagesOptions Options = new();

    /// <summary>
    /// Sends every <see cref="UriImageSource"/> through Spine's image cache and registers
    /// <see cref="IImageCache"/> and <see cref="ImageOptions"/>. <c>UseSpine()</c> calls it for an app that
    /// references this package; call it yourself to change the options (before or after <c>UseSpine()</c>),
    /// or in an app without Spine's core. The order relative to <c>UseMauiApp</c> does not matter. Calling it
    /// more than once only applies the options.
    /// </summary>
    public static MauiAppBuilder UseSpineImages(this MauiAppBuilder builder, Action<SpineImagesOptions>? configure = null)
    {
        configure?.Invoke(Options);

        if (builder.Services.Any(static s => s.ServiceType == typeof(IImageCache)))
            return builder;

        builder.Services.TryAddSingleton(Options);
        builder.Services.AddSingleton<IImageCache>(static services =>
            SpineImageCache.Create(services.GetRequiredService<SpineImagesOptions>(), services.GetService<ILoggerFactory>()?.CreateLogger<IImageCache>()));

#if IOS || MACCATALYST || WINDOWS
        // MAUI's image source factory creates services without the app's container, so the service finds the
        // cache through SpineImageCache.Shared.
        builder.OverrideImageSourceService<UriImageSource, SpineUriImageSourceService>();
#endif
        ImageOptions.Configure();
        return builder;
    }
}
