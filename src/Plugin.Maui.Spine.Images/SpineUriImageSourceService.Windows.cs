#if WINDOWS

using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using WImageSource = Microsoft.UI.Xaml.Media.ImageSource;

namespace Plugin.Maui.Spine.Images;

/// <summary>
/// Loads a <see cref="UriImageSource"/> through the disk cache and decodes it with
/// <see cref="BitmapImage.DecodePixelWidth"/> or <see cref="BitmapImage.DecodePixelHeight"/> for the view's box.
/// </summary>
internal sealed class SpineUriImageSourceService : ImageSourceService, IImageSourceService<UriImageSource>
{
    // MAUI's image source factory creates services with a parameterless constructor.
    public SpineUriImageSourceService()
    {
    }

    public override Task<IImageSourceServiceResult<WImageSource>?> GetImageSourceAsync(IImageSource imageSource, float scale = 1, CancellationToken cancellationToken = default)
    {
        var target = ImageTarget.TakePending();
        return imageSource is UriImageSource { IsEmpty: false } source
            ? LoadAsync(source, target, cancellationToken)
            : Task.FromResult<IImageSourceServiceResult<WImageSource>?>(null);
    }

    // No ConfigureAwait(false): a BitmapImage belongs to the UI thread it is created on.
    static async Task<IImageSourceServiceResult<WImageSource>?> LoadAsync(UriImageSource source, ImageTarget target, CancellationToken cancellationToken)
    {
        var file = await SpineImageCache.Shared.Files.GetAsync(source.Uri, source.CacheValidity, source.CachingEnabled, cancellationToken);

        using var stream = file.OpenRead().AsRandomAccessStream();
        var image = new BitmapImage();
        if (!target.IsFull)
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            int width = (int)decoder.OrientedPixelWidth, height = (int)decoder.OrientedPixelHeight;
            var max = target.MaxPixelSize(width, height);
            if (max < Math.Max(width, height))
            {
                image.DecodePixelType = DecodePixelType.Physical;
                if (width >= height)
                    image.DecodePixelWidth = max;
                else
                    image.DecodePixelHeight = max;
            }
            stream.Seek(0);
        }

        await image.SetSourceAsync(stream);
        return new ImageSourceServiceResult(image);
    }
}

#endif
