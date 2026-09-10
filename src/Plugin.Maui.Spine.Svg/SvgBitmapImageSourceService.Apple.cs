#if IOS || MACCATALYST

using Foundation;
using UIKit;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Decodes an <see cref="SvgBitmapImageSource"/> at its own scale. MAUI's stream service would
/// decode it at scale 1, which is what made every SVG icon soft on a 2× or 3× screen.
/// </summary>
internal sealed class SvgBitmapImageSourceService : ImageSourceService, IImageSourceService<SvgBitmapImageSource>
{
    // MAUI's image source factory instantiates services with Activator.CreateInstance, which needs
    // a real parameterless constructor; an optional logger parameter does not count.
    public SvgBitmapImageSourceService()
    {
    }

    public override async Task<IImageSourceServiceResult<UIImage>?> GetImageAsync(IImageSource imageSource, float scale = 1, CancellationToken cancellationToken = default)
    {
        if (imageSource is not SvgBitmapImageSource source || source.IsEmpty)
            return null;

        using var stream = await ((IStreamImageSource)source).GetStreamAsync(cancellationToken).ConfigureAwait(false);
        using var data = NSData.FromStream(stream)
            ?? throw new InvalidOperationException($"The bitmap rendered from \"{source.ResourceName}\" could not be read.");

        var image = UIImage.LoadFromData(data, source.Scale)
            ?? throw new InvalidOperationException($"The bitmap rendered from \"{source.ResourceName}\" is not a PNG iOS can decode.");

        return new ImageSourceServiceResult(image, image.Dispose);
    }
}

#endif
