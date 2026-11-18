namespace Plugin.Maui.Spine.Controls;

/// <summary>One part of a <see cref="BarChart"/> with <see cref="BarChart.Segments"/>: a value and its colour.</summary>
public sealed record ChartSegment(double Value, Color Color);

/// <summary>
/// A share as a bar: the track the whole width, the value from the leading edge, round ends. 6 points tall by default.
/// <para>
/// With <see cref="Segments"/> the bar is parts stacked from the leading edge (or the top), each its share of their
/// total in its own colour, such as the time spent in each glucose range. <see cref="Orientation"/>
/// <c>Vertical</c> stands the bar up: a single <see cref="Value"/> then fills from the bottom.
/// </para>
/// </summary>
public sealed class BarChart : ChartView
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(double), typeof(BarChart), 0.0, propertyChanged: Redraw);

    public static readonly BindableProperty SegmentsProperty =
        BindableProperty.Create(nameof(Segments), typeof(IList<ChartSegment>), typeof(BarChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty OrientationProperty =
        BindableProperty.Create(nameof(Orientation), typeof(StackOrientation), typeof(BarChart), StackOrientation.Horizontal,
            propertyChanged: static (b, _, _) => ((BarChart)b).OnOrientationChanged());

    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(double?), typeof(BarChart), null, propertyChanged: Redraw);

    /// <summary>The share, 0 to 1. Not drawn when <see cref="Segments"/> is set.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>
    /// Parts stacked from the leading edge, or the top when <see cref="Orientation"/> is <c>Vertical</c>, together
    /// filling the bar. A part above 0 keeps a sliver however small its share; a part of 0 is not drawn. None when null.
    /// </summary>
    public IList<ChartSegment>? Segments { get => (IList<ChartSegment>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }

    /// <summary>
    /// <c>Horizontal</c> (the default) or <c>Vertical</c>, a bar standing up. A vertical bar is 6 points wide by
    /// default; give it a height.
    /// </summary>
    public StackOrientation Orientation { get => (StackOrientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    /// <summary>The ends' radius; half the bar's thickness, fully round, when null.</summary>
    public double? CornerRadius { get => (double?)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    const float LeastSegment = 3;

    public BarChart() => HeightRequest = 6;

    void OnOrientationChanged()
    {
        if (Orientation == StackOrientation.Vertical && HeightRequest == 6 && WidthRequest < 0)
        {
            HeightRequest = -1;
            WidthRequest = 6;
        }
        Invalidate();
    }

    public override void Draw(ICanvas canvas, RectF rect)
    {
        var vertical = Orientation == StackOrientation.Vertical;
        var thickness = vertical ? rect.Width : rect.Height;
        var length = vertical ? rect.Height : rect.Width;
        var radius = (float)(CornerRadius ?? thickness / 2);
        canvas.FillColor = Track;
        canvas.FillRoundedRectangle(rect, radius);

        if (Segments is { Count: > 0 } segments)
        {
            // The parts are plain rectangles inside the bar's rounded shape, so only the bar's ends are round.
            var path = new PathF();
            path.AppendRoundedRectangle(rect, radius);
            canvas.SaveState();
            canvas.ClipPath(path);
            var lengths = BarSegments.Lengths([.. segments.Select(s => s.Value)], length, LeastSegment);
            var at = 0f;
            for (var i = 0; i < segments.Count; i++)
            {
                if (lengths[i] <= 0)
                    continue;
                canvas.FillColor = segments[i].Color;
                // Overlapping the next by a hair, so no seam of the track shows between two parts.
                canvas.FillRectangle(vertical
                    ? new RectF(rect.X, rect.Y + at, thickness, lengths[i] + 0.5f)
                    : new RectF(rect.X + at, rect.Y, lengths[i] + 0.5f, thickness));
                at += lengths[i];
            }
            canvas.RestoreState();
            return;
        }

        if (Value <= 0)
            return;
        var filled = Math.Max(Math.Min(thickness, length), length * (float)Math.Min(Value, 1));
        canvas.FillColor = Color;
        canvas.FillRoundedRectangle(vertical
            ? new RectF(rect.X, rect.Bottom - filled, thickness, filled)
            : new RectF(rect.X, rect.Y, filled, thickness), radius);
    }
}
