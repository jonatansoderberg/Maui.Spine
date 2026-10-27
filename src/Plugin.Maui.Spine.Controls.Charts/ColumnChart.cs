namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Values as columns with a label under each, such as the cost per day of the last week: each column's height is its
/// share of the highest value, and a value of 0 shows only the track. The last label is bold (today), unless
/// <see cref="HighlightLast"/> is false. 72 points tall by default.
/// <para>
/// With <see cref="ShowTrack"/> off there is no full-height track behind the columns: a value of 0 is a short column
/// at the bottom in the track's colour, so a week with one busy day doesn't read as one lit column among empty ones.
/// </para>
/// <para>
/// With <see cref="ValueLabels"/>, pointing at a column (or tapping it, on touch) picks it out: the others dim, its
/// value shows above it in a row the chart keeps for that, and the system tooltip says the label and the value (or
/// the column's <see cref="ToolTips"/> entry). <see cref="SelectedIndex"/> is the column picked out, or -1.
/// </para>
/// </summary>
public sealed class ColumnChart : ChartView
{
    public static readonly BindableProperty ValuesProperty =
        BindableProperty.Create(nameof(Values), typeof(IList<double>), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty LabelsProperty =
        BindableProperty.Create(nameof(Labels), typeof(IList<string>), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty ShowTrackProperty =
        BindableProperty.Create(nameof(ShowTrack), typeof(bool), typeof(ColumnChart), true, propertyChanged: Redraw);

    public static readonly BindableProperty HighlightLastProperty =
        BindableProperty.Create(nameof(HighlightLast), typeof(bool), typeof(ColumnChart), true, propertyChanged: Redraw);

    public static readonly BindableProperty ValueLabelsProperty =
        BindableProperty.Create(nameof(ValueLabels), typeof(IList<string>), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty ToolTipsProperty =
        BindableProperty.Create(nameof(ToolTips), typeof(IList<string>), typeof(ColumnChart), null);

    static readonly BindablePropertyKey SelectedIndexPropertyKey =
        BindableProperty.CreateReadOnly(nameof(SelectedIndex), typeof(int), typeof(ColumnChart), -1, propertyChanged: Redraw);

    public static readonly BindableProperty SelectedIndexProperty = SelectedIndexPropertyKey.BindableProperty;

    /// <summary>Each column's value as text, such as "$0.32", shown above the column it picks out.</summary>
    public IList<string>? ValueLabels { get => (IList<string>?)GetValue(ValueLabelsProperty); set => SetValue(ValueLabelsProperty, value); }

    /// <summary>A longer text per column for the system tooltip; the label and the value when null.</summary>
    public IList<string>? ToolTips { get => (IList<string>?)GetValue(ToolTipsProperty); set => SetValue(ToolTipsProperty, value); }

    /// <summary>The column the pointer is on (or that was tapped), or -1.</summary>
    public int SelectedIndex => (int)GetValue(SelectedIndexProperty);

    public static readonly BindableProperty FontFamilyProperty =
        BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(ColumnChart), null, propertyChanged: Redraw);

    /// <summary>The values, oldest first.</summary>
    public IList<double>? Values { get => (IList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    /// <summary>One label per value, under its column; none when null.</summary>
    public IList<string>? Labels { get => (IList<string>?)GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }

    /// <summary>
    /// Whether each column has a full-height track behind it. True by default; off, a value of 0 is a short column in
    /// the track's colour.
    /// </summary>
    public bool ShowTrack { get => (bool)GetValue(ShowTrackProperty); set => SetValue(ShowTrackProperty, value); }

    /// <summary>Whether the last label is bold, as today in a week. True by default.</summary>
    public bool HighlightLast { get => (bool)GetValue(HighlightLastProperty); set => SetValue(HighlightLastProperty, value); }

    /// <summary>The labels' font, or null for the platform's.</summary>
    public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public ColumnChart()
    {
        HeightRequest = 72;
        var pointer = new PointerGestureRecognizer();
        pointer.PointerMoved += (_, e) => Select(e.GetPosition(this) is { } p ? IndexAt(p.X) : -1);
        pointer.PointerExited += (_, _) => Select(-1);
        GestureRecognizers.Add(pointer);
        // Touch has no pointer to rest: a tap picks a column out, and a second tap on it lets it go.
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            var index = e.GetPosition(this) is { } p ? IndexAt(p.X) : -1;
            Select(index == SelectedIndex ? -1 : index);
        };
        GestureRecognizers.Add(tap);
    }

    int IndexAt(double x) =>
        Values is { Count: > 0 } values && Width > 0 ? Math.Clamp((int)(x / (Width / values.Count)), 0, values.Count - 1) : -1;

    void Select(int index)
    {
        if (ValueLabels is null || index == SelectedIndex)
            return;
        SetValue(SelectedIndexPropertyKey, index);
        ToolTipProperties.SetText(this, index < 0 ? null : ToolTipFor(index));
    }

    string? ToolTipFor(int index)
    {
        if (ToolTips is { } tips && index < tips.Count)
            return tips[index];
        var label = Labels is { } names && index < names.Count ? names[index] : null;
        var value = ValueLabels is { } values && index < values.Count ? values[index] : null;
        return label is null ? value : value is null ? label : $"{label}: {value}";
    }

    const float LabelHeight = 16, Gap = 8, MinColumn = 4, MaxColumnWidth = 28, DimmedAlpha = 0.4f;

    public override void Draw(ICanvas canvas, RectF rect)
    {
        if (Values is not { Count: > 0 } values)
            return;

        var count = values.Count;
        var slot = rect.Width / count;
        var columnWidth = Math.Min(MaxColumnWidth, slot - Gap);
        var labels = Labels is { Count: > 0 };
        // A row above the columns for the value of the one picked out, kept whether or not one is, so nothing moves.
        var valueRow = ValueLabels is { Count: > 0 } ? LabelHeight + 2 : 0;
        var top = rect.Y + valueRow;
        var height = rect.Height - valueRow - (labels ? LabelHeight + 4 : 0);
        var max = values.Max();
        var dark = Application.Current?.RequestedTheme != AppTheme.Light;
        var selected = SelectedIndex;

        for (var i = 0; i < count; i++)
        {
            var x = rect.X + slot * i + (slot - columnWidth) / 2;
            var dimmed = selected >= 0 && i != selected;
            if (ShowTrack)
            {
                canvas.FillColor = Track;
                canvas.FillRoundedRectangle(new RectF(x, top, columnWidth, height), 4);
            }
            else if (max <= 0 || values[i] <= 0)
            {
                canvas.FillColor = dimmed ? Track.WithAlpha(DimmedAlpha) : Track;
                canvas.FillRoundedRectangle(new RectF(x, top + height - MinColumn, columnWidth, MinColumn), 2);
            }
            if (max > 0 && values[i] > 0)
            {
                var h = Math.Max(MinColumn, height * (float)(values[i] / max));
                canvas.FillColor = dimmed ? Color.WithAlpha(DimmedAlpha) : Color;
                canvas.FillRoundedRectangle(new RectF(x, top + height - h, columnWidth, h), 4);
            }

            if (i == selected && ValueLabels is { } valueLabels && i < valueLabels.Count)
            {
                // Centred on its column; moved in only as far as the text would otherwise leave the chart.
                var font = new Microsoft.Maui.Graphics.Font(FontFamily, FontWeights.Bold);
                const float size = 11;
                var width = canvas.GetStringSize(valueLabels[i], font, size).Width + 2;
                var centre = rect.X + slot * i + slot / 2;
                var left = Math.Clamp(centre - width / 2, rect.X, Math.Max(rect.X, rect.Right - width));
                canvas.Font = font;
                canvas.FontSize = size;
                canvas.FontColor = dark ? Colors.White : Colors.Black;
                canvas.DrawString(valueLabels[i], left, rect.Y, width, LabelHeight, HorizontalAlignment.Center, VerticalAlignment.Center);
            }

            if (Labels is { } names && i < names.Count)
            {
                var strong = selected >= 0 ? i == selected : HighlightLast && i == count - 1;
                canvas.Font = new Microsoft.Maui.Graphics.Font(FontFamily, strong ? FontWeights.Bold : FontWeights.Regular);
                canvas.FontSize = 11;
                canvas.FontColor = strong ? (dark ? Colors.White : Colors.Black) : dark ? Color.FromArgb("#8D8D93") : Color.FromArgb("#6D6D72");
                canvas.DrawString(names[i], rect.X + slot * i, top + height + 4, slot, LabelHeight,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }
}
