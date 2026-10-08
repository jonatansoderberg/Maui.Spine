#if WINDOWS

using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using WImage = Microsoft.UI.Xaml.Controls.Image;

namespace Plugin.Maui.Spine.Images;

public static partial class ImageOptions
{
    // MAUI keeps the old source until the new image has loaded, as on Apple; Windows has no memory cache to
    // check, so the placeholder always goes in first.
    static partial void ShowBeforeLoad(IImageHandler handler, UriImageSource source, ImageTarget target, Placeholder placeholder)
    {
        if (handler.PlatformView is not WImage view)
            return;

        var bitmap = new WriteableBitmap(placeholder.Width, placeholder.Height);
        var bgra = new byte[placeholder.Rgba.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = placeholder.Rgba[i + 2];
            bgra[i + 1] = placeholder.Rgba[i + 1];
            bgra[i + 2] = placeholder.Rgba[i];
            bgra[i + 3] = 255;
        }
        using (var pixels = bitmap.PixelBuffer.AsStream())
            pixels.Write(bgra);
        bitmap.Invalidate();
        view.Source = bitmap;
    }
}

#endif
