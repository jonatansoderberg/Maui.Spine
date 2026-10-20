namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A share as a ring: the track all round, the value clockwise from 12 o'clock with round ends. Null draws only the
/// track (unknown, which is not the same as 0). Put a label over it in a Grid for the number.
/// </summary>
public sealed class RingChart : ChartView
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(double?), typeof(RingChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty ThicknessProperty =
        BindableProperty.Create(nameof(Thickness), typeof(double), typeof(RingChart), 10.0, propertyChanged: Redraw);

    /// <summary>The share, 0 to 1; null for unknown.</summary>
    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>The ring's width. 10 by default.</summary>
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public override void Draw(ICanvas canvas, RectF rect)
    {
        var thickness = (float)Thickness;
        var side = Math.Min(rect.Width, rect.Height);
        var ring = new RectF(rect.Center.X - side / 2 + thickness / 2, rect.Center.Y - side / 2 + thickness / 2, side - thickness, side - thickness);
        canvas.StrokeSize = thickness;
        canvas.StrokeColor = Track;
        canvas.DrawEllipse(ring);
        if (Value is not { } value || value <= 0)
            return;

        var sweep = (float)Math.Min(value, 1) * 360;
        canvas.StrokeColor = Color;
        canvas.StrokeLineCap = LineCap.Round;
        if (sweep >= 359.9f)
            canvas.DrawEllipse(ring);
        else
            canvas.DrawArc(ring, 90, 90 - sweep, true, false);
    }
}
