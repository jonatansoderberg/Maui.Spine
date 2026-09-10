using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>Which way a <see cref="WidgetGradient"/> runs.</summary>
public enum WidgetGradientDirection
{
    /// <summary>Top to bottom.</summary>
    Vertical,

    /// <summary>Leading edge to trailing edge.</summary>
    Horizontal,

    /// <summary>Top leading corner to bottom trailing corner.</summary>
    Diagonal,
}

/// <summary>
/// A linear gradient for a widget's surface; see <see cref="WidgetTimeline.Background(WidgetGradient)"/>.
/// The colors are spread evenly, the first at the start.
/// </summary>
public sealed record WidgetGradient
{
    /// <summary>Creates a gradient.</summary>
    /// <param name="colors">Two or more colors, from the start to the end.</param>
    /// <param name="direction">Which way it runs; top to bottom by default.</param>
    /// <exception cref="ArgumentException">Fewer than two colors.</exception>
    [JsonConstructor]
    public WidgetGradient(IReadOnlyList<WidgetColor> colors, WidgetGradientDirection direction = WidgetGradientDirection.Vertical)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count < 2) throw new ArgumentException("A gradient needs at least two colors.", nameof(colors));

        Colors = [.. colors];
        Direction = direction;
    }

    /// <summary>The colors from the start to the end.</summary>
    public IReadOnlyList<WidgetColor> Colors { get; }

    /// <summary>Which way the gradient runs.</summary>
    public WidgetGradientDirection Direction { get; }
}
