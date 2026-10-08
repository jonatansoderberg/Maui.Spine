#if ANDROID

using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;

namespace Plugin.Maui.Spine.Images;

public static partial class ImageOptions
{
    // Glide clears the view when it starts a load on it (MAUI's target sets the drawable to null), and sets
    // an image it has in memory before the mapping returns. So the placeholder goes in afterwards, and only
    // into a view Glide left empty.
    static partial void ShowAfterLoadStarted(IImageHandler handler, Placeholder placeholder)
    {
        if (handler.PlatformView is not ImageView { Drawable: null } view)
            return;

        var colors = new int[placeholder.Width * placeholder.Height];
        var rgba = placeholder.Rgba;
        for (var i = 0; i < colors.Length; i++)
            colors[i] = unchecked((int)0xFF000000) | rgba[i * 4] << 16 | rgba[i * 4 + 1] << 8 | rgba[i * 4 + 2];

        var bitmap = Bitmap.CreateBitmap(colors, placeholder.Width, placeholder.Height, Bitmap.Config.Argb8888!);
        var drawable = new BitmapDrawable(view.Resources, bitmap);
        drawable.SetFilterBitmap(true);
        view.SetImageDrawable(drawable);
    }
}

#endif
