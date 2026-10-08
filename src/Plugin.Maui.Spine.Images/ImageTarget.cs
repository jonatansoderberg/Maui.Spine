namespace Plugin.Maui.Spine.Images;

/// <summary>
/// The box, in pixels, an image is decoded for and how it fills it. Zero means no box: the image is
/// decoded at its own size.
/// </summary>
internal readonly record struct ImageTarget(int Width, int Height, Aspect Aspect)
{
    // MAUI's image source services are given no view, only the source. The Source mapping knows the view,
    // and MAUI calls the service synchronously from it, so the mapping leaves the box here on the main
    // thread and the service takes it before its first await.
    [ThreadStatic]
    static ImageTarget? _pending;

    public static ImageTarget Full => new(0, 0, Aspect.AspectFit);

    public bool IsFull => Width <= 0 || Height <= 0;

    public static void SetPending(ImageTarget? target) => _pending = target;

    /// <summary>The box the Source mapping left, or the screen when the image is not in an <see cref="Image"/>.</summary>
    public static ImageTarget TakePending()
    {
        var target = _pending;
        _pending = null;
        return target ?? Screen(Aspect.AspectFit);
    }

    /// <summary>
    /// The box <paramref name="image"/> is shown in: its requested size, or else its laid-out size, or else
    /// the screen's width or height. <see cref="Full"/> when <see cref="ImageOptions.DownsampleProperty"/> is off.
    /// </summary>
    public static ImageTarget For(Image image)
    {
        if (!ImageOptions.GetDownsample(image))
            return Full;

        var display = DeviceDisplay.Current.MainDisplayInfo;
        var density = display.Density > 0 ? display.Density : 1;
        return new(
            Side(image.WidthRequest, image.Width, density, display.Width),
            Side(image.HeightRequest, image.Height, density, display.Height),
            image.Aspect);

        static int Side(double requested, double laidOut, double density, double screen) =>
            (int)Math.Ceiling(requested > 0 ? requested * density : laidOut > 0 ? laidOut * density : screen);
    }

    static ImageTarget Screen(Aspect aspect)
    {
        var display = DeviceDisplay.Current.MainDisplayInfo;
        return new((int)display.Width, (int)display.Height, aspect);
    }

    /// <summary>
    /// The longest side to decode an image of <paramref name="imageWidth"/> × <paramref name="imageHeight"/>
    /// pixels at, so that it still covers the box as its aspect asks; never more than the image itself.
    /// </summary>
    public int MaxPixelSize(int imageWidth, int imageHeight)
    {
        var longest = Math.Max(imageWidth, imageHeight);
        if (IsFull || Aspect == Aspect.Center || imageWidth <= 0 || imageHeight <= 0)
            return longest;

        var scaleX = (double)Width / imageWidth;
        var scaleY = (double)Height / imageHeight;
        var scale = Aspect == Aspect.AspectFit ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
        return Math.Clamp((int)Math.Ceiling(scale * longest), 1, longest);
    }

    /// <summary>The memory cache's key for <paramref name="uri"/> decoded for this box.</summary>
    public string Key(Uri uri) => IsFull ? uri.AbsoluteUri : $"{uri.AbsoluteUri} {Width}x{Height} {Aspect}";
}
