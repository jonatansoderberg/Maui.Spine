namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Values as columns with a label under each, such as the cost per day of the last week: each column's height is its
/// share of the highest value, and a value of 0 shows only the track. The last label is bold (today), unless
/// <see cref="HighlightLast"/> is false. 72 points tall by default.
/// </summary>
public sealed class ColumnChart : ChartView
{
    public static readonly BindableProperty ValuesProperty =
        BindableProperty.Create(nameof(Values), typeof(IList<double>), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty LabelsProperty =
        BindableProperty.Create(nameof(Labels), typeof(IList<string>), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty HighlightLastProperty =
        BindableProperty.Create(nameof(HighlightLast), typeof(bool), typeof(ColumnChart), true, propertyChanged: Redraw);

    public static readonly BindableProperty FontFamilyProperty =
        BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(ColumnChart), null, propertyChanged: Redraw);

    /// <summary>The values, oldest first.</summary>
    public IList<double>? Values { get => (IList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    /// <summary>One label per value, under its column; none when null.</summary>
    public IList<string>? Labels { get => (IList<string>?)GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }

    /// <summary>Whether the last label is bold, as today in a week. True by default.</summary>
    public bool HighlightLast { get => (bool)GetValue(HighlightLastProperty); set => SetValue(HighlightLastProperty, value); }

    /// <summary>The labels' font, or null for the platform's.</summary>
    public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public ColumnChart() => HeightRequest = 72;

    const float LabelHeight = 16, Gap = 8, MinColumn = 4, MaxColumnWidth = 28;

    public override void Draw(ICanvas canvas, RectF rect)
    {
        if (Values is not { Count: > 0 } values)
            return;

        var count = values.Count;
        var slot = rect.Width / count;
        var columnWidth = Math.Min(MaxColumnWidth, slot - Gap);
        var labels = Labels is { Count: > 0 };
        var height = rect.Height - (labels ? LabelHeight + 4 : 0);
        var max = values.Max();
        var dark = Application.Current?.RequestedTheme != AppTheme.Light;

        for (var i = 0; i < count; i++)
        {
            var x = rect.X + slot * i + (slot - columnWidth) / 2;
            canvas.FillColor = Track;
            canvas.FillRoundedRectangle(new RectF(x, rect.Y, columnWidth, height), 4);
            if (max > 0 && values[i] > 0)
            {
                var h = Math.Max(MinColumn, height * (float)(values[i] / max));
                canvas.FillColor = Color;
                canvas.FillRoundedRectangle(new RectF(x, rect.Y + height - h, columnWidth, h), 4);
            }

            if (Labels is { } names && i < names.Count)
            {
                var strong = HighlightLast && i == count - 1;
                canvas.Font = new Microsoft.Maui.Graphics.Font(FontFamily, strong ? FontWeights.Bold : FontWeights.Regular);
                canvas.FontSize = 11;
                canvas.FontColor = strong ? (dark ? Colors.White : Colors.Black) : dark ? Color.FromArgb("#8D8D93") : Color.FromArgb("#6D6D72");
                canvas.DrawString(names[i], rect.X + slot * i, rect.Y + height + 4, slot, LabelHeight,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }
}
