#if ANDROID

using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Decodes an <see cref="SvgBitmapImageSource"/> and stamps the bitmap with the density it was
/// rendered for, so the drawable keeps the icon's dp size instead of stretching a 1× bitmap.
/// </summary>
internal sealed class SvgBitmapImageSourceService : ImageSourceService, IImageSourceService<SvgBitmapImageSource>
{
    // MAUI's image source factory instantiates services with Activator.CreateInstance, which needs
    // a real parameterless constructor; an optional logger parameter does not count.
    public SvgBitmapImageSourceService()
    {
    }

    public override async Task<IImageSourceServiceResult<Drawable>?> GetDrawableAsync(IImageSource imageSource, Context context, CancellationToken cancellationToken = default)
    {
        if (imageSource is not SvgBitmapImageSource source || source.IsEmpty)
            return null;

        using var stream = await ((IStreamImageSource)source).GetStreamAsync(cancellationToken).ConfigureAwait(false);
        var bitmap = await BitmapFactory.DecodeStreamAsync(stream).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The bitmap rendered from \"{source.ResourceName}\" is not a PNG Android can decode.");

        bitmap.Density = (int)Math.Round(160 * source.Scale);
        var drawable = new BitmapDrawable(context.Resources, bitmap);

        return new ImageSourceServiceResult(drawable, () =>
        {
            drawable.Dispose();
            bitmap.Dispose();
        });
    }
}

#endif
