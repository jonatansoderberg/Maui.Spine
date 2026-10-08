#if IOS || MACCATALYST

using CoreGraphics;
using UIKit;

namespace Plugin.Maui.Spine.Images;

public static partial class ImageOptions
{
    // MAUI leaves the old image in place until the new one has loaded, so the placeholder goes in first
    // and stays until MAUI replaces it. Skipped for an image in memory, which MAUI sets in this same pass.
    static partial void ShowBeforeLoad(IImageHandler handler, UriImageSource source, ImageTarget target, Placeholder placeholder)
    {
        if (handler.PlatformView is not UIImageView view
            || source.CachingEnabled && SpineImageCache.Shared.FromMemory(target.Key(source.Uri)) is not null)
            return;

        view.AnimationImages = null;
        view.Image = ToImage(placeholder);
    }

    static UIImage ToImage(Placeholder placeholder)
    {
        using var colorSpace = CGColorSpace.CreateSrgb();
        using var provider = new CGDataProvider(placeholder.Rgba);
        using var cg = new CGImage(placeholder.Width, placeholder.Height, 8, 32, placeholder.Width * 4, colorSpace,
            CGBitmapFlags.NoneSkipLast | CGBitmapFlags.ByteOrderDefault, provider, null, false, CGColorRenderingIntent.Default);
        return new UIImage(cg);
    }
}

#endif
