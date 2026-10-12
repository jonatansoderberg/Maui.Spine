using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Everything a platform renderer needs to draw one kind on one view. <c>CornerRadius</c> is
/// <see langword="null"/> when the platform view's own rounding applies; <c>Still</c> means Reduce Motion is on.
/// </summary>
internal sealed record HighlightLook(
    HighlightKind Kind,
    IReadOnlyList<Color> Colors,
    double Intensity,
    double Outset,
    double? CornerRadius,
    Size Size,
    TimeSpan Cycle,
    bool Still)
{
    /// <summary>The spectrum Edge uses by default, as on the concept page.</summary>
    public static IReadOnlyList<Color> Spectrum { get; } =
    [
        Color.FromArgb("#5B8CFF"),
        Color.FromArgb("#B56CFF"),
        Color.FromArgb("#FF6FA8"),
        Color.FromArgb("#FFB259"),
        Color.FromArgb("#5BE0C4"),
    ];

    public bool SameAs(HighlightLook? other) =>
        other is not null
        && Kind == other.Kind
        && Still == other.Still
        && Size == other.Size
        && CornerRadius == other.CornerRadius
        && Outset == other.Outset
        && Intensity == other.Intensity
        && Colors.SequenceEqual(other.Colors);

    /// <summary>The rounding to draw with: at most half the shorter side, so a capsule stays a capsule.</summary>
    public double Radius(double platformRadius) =>
        Math.Clamp(CornerRadius ?? platformRadius, 0, Math.Min(Size.Width, Size.Height) / 2);

    /// <summary>The first colour: the one colour of every kind but Edge. Sheen's is white unless set.</summary>
    public Color Primary => Colors[0];

    public static HighlightLook For(View view, HighlightKind kind)
    {
        var colors = Highlight.GetColors(view) is { Count: > 0 } own ? own
            : kind == HighlightKind.Edge ? Spectrum
            : kind == HighlightKind.Sheen ? [Microsoft.Maui.Graphics.Colors.White]
            : [Accent()];

        return new HighlightLook(
            kind,
            colors,
            Highlight.GetIntensity(view),
            Highlight.GetOutset(view),
            CornerRadiusOf(view),
            new Size(Math.Max(0, view.Width), Math.Max(0, view.Height)),
            HighlightCycles.Cycle(kind),
            ReducedMotion.IsOn);
    }

    static Color Accent()
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        return SpineTheme.GetAccent(theme)
            ?? (theme == AppTheme.Dark ? Color.FromArgb("#7D9BFF") : Color.FromArgb("#2F5BEA"));
    }

    internal static double? CornerRadiusOf(View view) => view switch
    {
        Border { StrokeShape: Microsoft.Maui.Controls.Shapes.RoundRectangle shape } => shape.CornerRadius.TopLeft,
        Border { StrokeShape: Microsoft.Maui.Controls.Shapes.Ellipse } => double.MaxValue,
        Border => 0,
        Button { CornerRadius: >= 0 } button => button.CornerRadius,
        ImageButton { CornerRadius: >= 0 } button => button.CornerRadius,
        BoxView box => box.CornerRadius.TopLeft,
        _ => null,
    };
}
