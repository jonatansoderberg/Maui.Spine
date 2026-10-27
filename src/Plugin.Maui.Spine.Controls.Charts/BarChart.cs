namespace Plugin.Maui.Spine.Controls;

/// <summary>A share as a bar: the track the whole width, the value from the leading edge, round ends. 6 points tall by default.</summary>
public sealed class BarChart : ChartView
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(double), typeof(BarChart), 0.0, propertyChanged: Redraw);

    /// <summary>The share, 0 to 1.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public BarChart() => HeightRequest = 6;

    public override void Draw(ICanvas canvas, RectF rect)
    {
        var radius = rect.Height / 2;
        canvas.FillColor = Track;
        canvas.FillRoundedRectangle(rect, radius);
        if (Value <= 0)
            return;
        canvas.FillColor = Color;
        canvas.FillRoundedRectangle(new RectF(rect.X, rect.Y, Math.Max(rect.Height, rect.Width * (float)Math.Min(Value, 1)), rect.Height), radius);
    }
}
