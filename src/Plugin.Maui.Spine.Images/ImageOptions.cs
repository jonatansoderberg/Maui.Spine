using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Images;

/// <summary>
/// Per-image options for an <see cref="Image"/> whose <see cref="Image.Source"/> is a remote
/// <see cref="UriImageSource"/>.
/// </summary>
/// <example>
/// <code>
/// &lt;Image Source="{Binding PhotoUrl}"
///        ImageOptions.BlurHash="{Binding PhotoHash}"
///        Aspect="AspectFill" HeightRequest="120" /&gt;
/// </code>
/// </example>
public static partial class ImageOptions
{
    static void Reload(BindableObject bindable, object oldValue, object newValue) =>
        (bindable as Image)?.Handler?.UpdateValue(nameof(IImage.Source));

    /// <summary>
    /// Attached property holding a BlurHash of the image, drawn in the view at once and replaced by the
    /// image when it has loaded. The view's own size decides the placeholder's: give the image a size, or a
    /// layout that sizes it. Not shown when the image is already in memory.
    /// </summary>
    public static readonly BindableProperty BlurHashProperty = BindableProperty.CreateAttached(
        "BlurHash", typeof(string), typeof(ImageOptions), null, propertyChanged: Reload);

    /// <summary>Gets the BlurHash drawn while <paramref name="view"/>'s image loads.</summary>
    public static string? GetBlurHash(BindableObject view) => (string?)view.GetValue(BlurHashProperty);

    /// <summary>Sets the BlurHash drawn while <paramref name="view"/>'s image loads.</summary>
    public static void SetBlurHash(BindableObject view, string? value) => view.SetValue(BlurHashProperty, value);

    /// <summary>
    /// Attached property: whether the image is decoded at the size the view shows it at rather than its own,
    /// which is what keeps a list of large photos light. On by default. The view's requested size counts,
    /// then its laid-out size, then the screen's. iOS, Mac Catalyst and Windows; Android's Glide always does it.
    /// </summary>
    public static readonly BindableProperty DownsampleProperty = BindableProperty.CreateAttached(
        "Downsample", typeof(bool), typeof(ImageOptions), true, propertyChanged: Reload);

    /// <summary>Gets whether <paramref name="view"/>'s image is decoded at the view's size.</summary>
    public static bool GetDownsample(BindableObject view) => (bool)view.GetValue(DownsampleProperty);

    /// <summary>Sets whether <paramref name="view"/>'s image is decoded at the view's size.</summary>
    public static void SetDownsample(BindableObject view, bool value) => view.SetValue(DownsampleProperty, value);

    internal static void Configure() => ImageHandler.Mapper.ModifyMapping(nameof(IImage.Source), MapSource);

    static void MapSource(IImageHandler handler, IImage image, Action<IElementHandler, IElement>? previous)
    {
        if (image is not Image view || view.Source is not UriImageSource { IsEmpty: false } source)
        {
            previous?.Invoke(handler, image);
            return;
        }

        var target = ImageTarget.For(view);

#if IOS || MACCATALYST || WINDOWS
        // As Glide does on Android: an image whose size comes from its layout waits for that layout, one
        // pass, rather than being decoded for the whole screen; so does its placeholder, whose proportions
        // are the view's. A view sized by its image is laid out at zero, which counts as no size and gets
        // the screen's.
        if (!target.IsFull && view.Width < 0 && !(view.WidthRequest > 0 && view.HeightRequest > 0))
        {
            WaitForLayout(view);
            return;
        }
#endif

        var placeholder = DecodePlaceholder(handler, view, target);
        ImageTarget.SetPending(target);
        try
        {
            if (placeholder is not null)
                ShowBeforeLoad(handler, source, target, placeholder);

            previous?.Invoke(handler, image);

            if (placeholder is not null)
                ShowAfterLoadStarted(handler, placeholder);
        }
        finally
        {
            ImageTarget.SetPending(null);
        }
    }

    static readonly ConditionalWeakTable<Image, object> Waiting = [];

    static void WaitForLayout(Image view)
    {
        if (Waiting.TryAdd(view, Waiting))
            view.SizeChanged += OnLaidOut;
    }

    static void OnLaidOut(object? sender, EventArgs e)
    {
        var view = (Image)sender!;
        view.SizeChanged -= OnLaidOut;
        Waiting.Remove(view);
        view.Handler?.UpdateValue(nameof(IImage.Source));
    }

    // A malformed hash from a server must not take the page down; the image still loads without it.
    static Placeholder? DecodePlaceholder(IImageHandler handler, Image view, ImageTarget target)
    {
        if (GetBlurHash(view) is not { Length: > 0 } hash)
            return null;

        if (!BlurHash.IsValid(hash))
        {
            handler.MauiContext?.Services.GetService<ILogger<Image>>()?.LogWarning("ImageOptions.BlurHash \"{Hash}\" is not a BlurHash; the image loads without a placeholder.", hash);
            return null;
        }

        var (width, height) = PlaceholderSize(target);
        return new(BlurHash.Decode(hash, width, height), width, height);
    }

    // 32 pixels on the long side, in the box's proportions, so the view's aspect does not crop the colours.
    internal static (int Width, int Height) PlaceholderSize(ImageTarget target)
    {
        const int Side = 32;
        if (target.IsFull)
            return (Side, Side);
        return target.Width >= target.Height
            ? (Side, Math.Max(1, (int)Math.Round(Side * (double)target.Height / target.Width)))
            : (Math.Max(1, (int)Math.Round(Side * (double)target.Width / target.Height)), Side);
    }

    static partial void ShowBeforeLoad(IImageHandler handler, UriImageSource source, ImageTarget target, Placeholder placeholder);

    static partial void ShowAfterLoadStarted(IImageHandler handler, Placeholder placeholder);
}

/// <summary>A decoded BlurHash: RGBA bytes, four per pixel.</summary>
internal sealed record Placeholder(byte[] Rgba, int Width, int Height);
