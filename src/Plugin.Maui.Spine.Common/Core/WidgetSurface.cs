namespace Plugin.Maui.Spine.Common;

/// <summary>
/// One entry's own surface, which replaces the timeline's whole surface while that entry is shown; see
/// <see cref="WidgetTimeline.Add(DateTimeOffset, WidgetNode, WidgetSurface?)"/>. A color or a gradient, and a
/// stored image over either — the same three the timeline takes with <see cref="WidgetTimeline.Background(WidgetColor)"/>,
/// <see cref="WidgetTimeline.Background(WidgetGradient)"/> and <see cref="WidgetTimeline.BackgroundImage"/>.
/// </summary>
public sealed record WidgetSurface
{
    /// <summary>A surface of <paramref name="color"/>, with <paramref name="image"/> over it when given.</summary>
    /// <param name="color">The color the widget is drawn on.</param>
    /// <param name="image">The id of an image stored with <see cref="IWidgetService.StoreAssetAsync"/>.</param>
    public WidgetSurface(WidgetColor color, string? image = null)
    {
        Color = color;
        Image = ValidImage(image);
    }

    /// <summary>A surface of <paramref name="gradient"/>, with <paramref name="image"/> over it when given.</summary>
    /// <param name="gradient">The gradient the widget is drawn on.</param>
    /// <param name="image">The id of an image stored with <see cref="IWidgetService.StoreAssetAsync"/>.</param>
    public WidgetSurface(WidgetGradient gradient, string? image = null)
    {
        ArgumentNullException.ThrowIfNull(gradient);
        Gradient = gradient;
        Image = ValidImage(image);
    }

    /// <summary>
    /// A surface of <paramref name="image"/> alone, over the platform's widget background — not the
    /// timeline's color, which this surface replaces with the rest.
    /// </summary>
    /// <param name="image">The id of an image stored with <see cref="IWidgetService.StoreAssetAsync"/>.</param>
    public WidgetSurface(string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        Image = image;
    }

    /// <summary>The color the widget is drawn on, when the surface has no <see cref="Gradient"/>.</summary>
    public WidgetColor? Color { get; }

    /// <summary>The gradient the widget is drawn on, when the surface has no <see cref="Color"/>.</summary>
    public WidgetGradient? Gradient { get; }

    /// <summary>The id of the stored image drawn over the color or gradient, scaled to fill and cropped.</summary>
    public string? Image { get; }

    private static string? ValidImage(string? image)
    {
        if (image is not null) ArgumentException.ThrowIfNullOrWhiteSpace(image);
        return image;
    }
}
