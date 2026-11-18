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
/// <para>
/// For a report: <see cref="Colors"/> gives each column its own colour, <see cref="ShowValueLabels"/> shows every
/// value above its column, <see cref="ReferenceValue"/> draws a dashed line across at a value with
/// <see cref="ReferenceLabel"/>, and <see cref="Maximum"/> fixes the top of the scale.
/// </para>
/// <para>
/// Along time: with <see cref="Times"/> (when each column begins), <see cref="From"/> and <see cref="To"/>, each column
/// stands over its own <see cref="ColumnSpan"/> of a time axis instead of sharing the width, so a
/// <see cref="ChartScroller"/> can move the columns along time; the scroller sets these and the values from its
/// source's <see cref="ChartColumn"/>s. Give it a fixed <see cref="Maximum"/> there, so loading more does not rescale.
/// </para>
/// </summary>
public sealed class ColumnChart : ChartView, IScrollableChart
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

    public static readonly BindableProperty ColorsProperty =
        BindableProperty.Create(nameof(Colors), typeof(IList<Color>), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty ShowValueLabelsProperty =
        BindableProperty.Create(nameof(ShowValueLabels), typeof(bool), typeof(ColumnChart), false, propertyChanged: Redraw);

    public static readonly BindableProperty ReferenceValueProperty =
        BindableProperty.Create(nameof(ReferenceValue), typeof(double?), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty ReferenceLabelProperty =
        BindableProperty.Create(nameof(ReferenceLabel), typeof(string), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty MaximumProperty =
        BindableProperty.Create(nameof(Maximum), typeof(double?), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty TimesProperty =
        BindableProperty.Create(nameof(Times), typeof(IList<DateTimeOffset>), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty ColumnSpanProperty =
        BindableProperty.Create(nameof(ColumnSpan), typeof(TimeSpan?), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty FromProperty =
        BindableProperty.Create(nameof(From), typeof(DateTimeOffset?), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty ToProperty =
        BindableProperty.Create(nameof(To), typeof(DateTimeOffset?), typeof(ColumnChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty ToolTipsProperty =
        BindableProperty.Create(nameof(ToolTips), typeof(IList<string>), typeof(ColumnChart), null);

    static readonly BindablePropertyKey SelectedIndexPropertyKey =
        BindableProperty.CreateReadOnly(nameof(SelectedIndex), typeof(int), typeof(ColumnChart), -1, propertyChanged: Redraw);

    public static readonly BindableProperty SelectedIndexProperty = SelectedIndexPropertyKey.BindableProperty;

    /// <summary>Each column's value as text, such as "$0.32", shown above the column it picks out.</summary>
    public IList<string>? ValueLabels { get => (IList<string>?)GetValue(ValueLabelsProperty); set => SetValue(ValueLabelsProperty, value); }

    /// <summary>A colour per column, such as the range its value falls in; <see cref="ChartView.Color"/> for a column without one.</summary>
    public IList<Color>? Colors { get => (IList<Color>?)GetValue(ColorsProperty); set => SetValue(ColorsProperty, value); }

    /// <summary>Whether every column shows its <see cref="ValueLabels"/> text above it, not only the one picked out. False by default.</summary>
    public bool ShowValueLabels { get => (bool)GetValue(ShowValueLabelsProperty); set => SetValue(ShowValueLabelsProperty, value); }

    /// <summary>A value to draw a dashed line across the columns at, such as a target's top; the scale reaches it. None when null.</summary>
    public double? ReferenceValue { get => (double?)GetValue(ReferenceValueProperty); set => SetValue(ReferenceValueProperty, value); }

    /// <summary>The reference line's text, at its trailing end, such as "10.0". None when null.</summary>
    public string? ReferenceLabel { get => (string?)GetValue(ReferenceLabelProperty); set => SetValue(ReferenceLabelProperty, value); }

    /// <summary>The value at the top of the columns, so charts side by side compare; the highest value (or the reference) when null.</summary>
    public double? Maximum { get => (double?)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <summary>When each column begins, one per value, oldest first: the columns then stand along the time from <see cref="From"/> to <see cref="To"/>.</summary>
    public IList<DateTimeOffset>? Times { get => (IList<DateTimeOffset>?)GetValue(TimesProperty); set => SetValue(TimesProperty, value); }

    /// <summary>How much time a column stands for along time, such as a day; the shortest step between two <see cref="Times"/> when null.</summary>
    public TimeSpan? ColumnSpan { get => (TimeSpan?)GetValue(ColumnSpanProperty); set => SetValue(ColumnSpanProperty, value); }

    /// <summary>The time at the leading edge, with <see cref="Times"/>.</summary>
    public DateTimeOffset? From { get => (DateTimeOffset?)GetValue(FromProperty); set => SetValue(FromProperty, value); }

    /// <summary>The time at the trailing edge, with <see cref="Times"/>.</summary>
    public DateTimeOffset? To { get => (DateTimeOffset?)GetValue(ToProperty); set => SetValue(ToProperty, value); }

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
        pointer.PointerMoved += (_, e) =>
        {
            if (_dragSelects)
                Select(e.GetPosition(this) is { } p ? IndexAt(p.X) : -1);
        };
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

    int IndexAt(double x)
    {
        if (Values is not { Count: > 0 } values || Width <= 0)
            return -1;
        if (Along is not { } along)
            return Math.Clamp((int)(x / (Width / values.Count)), 0, values.Count - 1);
        for (var i = 0; i < values.Count; i++)
        {
            var (left, width) = along.Slot(i, 0);
            if (x >= left && x < left + width && left + width > 0 && left < along.Right)
                return i;
        }
        return -1;
    }

    bool _dragSelects = true;
    float _referenceRoom;

    /// <summary>The columns along time: where each column's time falls across the width.</summary>
    sealed record TimeLayout(IList<DateTimeOffset> Times, TimeSpan Span, DateTimeOffset From, DateTimeOffset To, float Right)
    {
        public float X(DateTimeOffset time, float left) => left + (float)((time - From).TotalSeconds / Math.Max(1, (To - From).TotalSeconds)) * Right;

        public (float Left, float Width) Slot(int index, float left)
        {
            var x = X(Times[index], left);
            return (x, X(Times[index] + Span, left) - x);
        }
    }

    /// <summary>The time layout, when the columns have times and the chart a time window; null for columns sharing the width.</summary>
    TimeLayout? Along
    {
        get
        {
            if (Times is not { Count: > 0 } times || Values is not { } values || times.Count != values.Count || From is not { } from || To is not { } to || to <= from)
                return null;
            var span = ColumnSpan ?? Shortest(times);
            return new TimeLayout(times, span, from, to, (float)Width - _referenceRoom);
        }
    }

    static TimeSpan Shortest(IList<DateTimeOffset> times)
    {
        var shortest = TimeSpan.MaxValue;
        for (var i = 1; i < times.Count; i++)
            if (times[i] - times[i - 1] is { } step && step > TimeSpan.Zero && step < shortest)
                shortest = step;
        return shortest == TimeSpan.MaxValue ? TimeSpan.FromDays(1) : shortest;
    }

    void IScrollableChart.ShowWindow(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    void IScrollableChart.ShowHistory(ChartSlice history)
    {
        var columns = history.Columns ?? [];
        // The texts only where the source gave them, so a chart without labels keeps none.
        Times = [.. columns.Select(c => c.Time)];
        Values = [.. columns.Select(c => c.Value)];
        Labels = columns.Any(c => c.Label is not null) ? [.. columns.Select(c => c.Label ?? "")] : null;
        ValueLabels = columns.Any(c => c.ValueLabel is not null) ? [.. columns.Select(c => c.ValueLabel ?? "")] : null;
        Colors = columns.Any(c => c.Color is not null) ? [.. columns.Select(c => c.Color ?? Color)] : null;
        ToolTips = columns.Any(c => c.ToolTip is not null) ? [.. columns.Select(c => c.ToolTip ?? "")] : null;
        Select(-1);
    }

    bool IScrollableChart.SelectsByDragging { get => _dragSelects; set => _dragSelects = value; }

    void IScrollableChart.ClearSelection() => Select(-1);

    Thickness IScrollableChart.PlotMargin => new(0, 0, _referenceRoom, 0);

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
        // Room at the trailing side for the reference line's label.
        var referenceRoom = ReferenceValue is not null && ReferenceLabel is { Length: > 0 } referenceLabel
            ? canvas.GetStringSize(referenceLabel, new Microsoft.Maui.Graphics.Font(FontFamily), 11).Width + 6
            : 0;
        _referenceRoom = referenceRoom;
        var slot = (rect.Width - referenceRoom) / count;
        var along = Along;
        var columnWidth = Math.Min(MaxColumnWidth, slot - Gap);
        var labels = Labels is { Count: > 0 };
        // A row above the columns for the value of the one picked out, kept whether or not one is, so nothing moves.
        var valueRow = ValueLabels is { Count: > 0 } ? LabelHeight + 2 : 0;
        var top = rect.Y + valueRow;
        var height = rect.Height - valueRow - (labels ? LabelHeight + 4 : 0);
        var max = ColumnScale.Top(values, ReferenceValue, Maximum);
        var dark = Application.Current?.RequestedTheme != AppTheme.Light;
        var selected = SelectedIndex;
        var valueTexts = new List<RectF>();
        // Along time the columns run past the edges, and are cut off there.
        if (along is not null)
        {
            canvas.SaveState();
            canvas.ClipRectangle(rect.X, rect.Y, rect.Width - referenceRoom, rect.Height);
        }

        for (var i = 0; i < count; i++)
        {
            var (slotLeft, slotWidth) = along?.Slot(i, rect.X) ?? (rect.X + slot * i, slot);
            if (along is not null)
            {
                if (slotLeft + slotWidth <= rect.X || slotLeft >= rect.Right - referenceRoom)
                    continue;
                columnWidth = Math.Min(MaxColumnWidth, slotWidth - Gap);
            }
            var x = slotLeft + (slotWidth - columnWidth) / 2;
            var dimmed = selected >= 0 && i != selected;
            var color = Colors is { } colors && i < colors.Count && colors[i] is { } own ? own : Color;
            var track = TrackColor ?? ChartColors.Track(color);
            if (ShowTrack)
            {
                canvas.FillColor = track;
                canvas.FillRoundedRectangle(new RectF(x, top, columnWidth, height), 4);
            }
            else if (max <= 0 || values[i] <= 0)
            {
                canvas.FillColor = dimmed ? track.WithAlpha(DimmedAlpha) : track;
                canvas.FillRoundedRectangle(new RectF(x, top + height - MinColumn, columnWidth, MinColumn), 2);
            }
            var h = ColumnScale.Height(values[i], max, height, MinColumn);
            if (h > 0)
            {
                canvas.FillColor = dimmed ? color.WithAlpha(DimmedAlpha) : color;
                canvas.FillRoundedRectangle(new RectF(x, top + height - h, columnWidth, h), 4);
            }

            if ((ShowValueLabels || i == selected) && ValueLabels is { } valueLabels && i < valueLabels.Count)
            {
                // Centred on its column; moved in only as far as the text would otherwise leave the chart. Shown for
                // every column, each sits just over its own column's top; the one picked out sits in the row on top.
                var font = new Microsoft.Maui.Graphics.Font(FontFamily, FontWeights.Bold);
                const float size = 11;
                var width = canvas.GetStringSize(valueLabels[i], font, size).Width + 2;
                var centre = slotLeft + slotWidth / 2;
                var left = Math.Clamp(centre - width / 2, rect.X, Math.Max(rect.X, rect.Right - width));
                var y = ShowValueLabels ? top + height - Math.Max(h, MinColumn) - LabelHeight - 1 : rect.Y;
                canvas.Font = font;
                canvas.FontSize = size;
                canvas.FontColor = dimmed ? (dark ? Microsoft.Maui.Graphics.Colors.White : Microsoft.Maui.Graphics.Colors.Black).WithAlpha(DimmedAlpha)
                    : dark ? Microsoft.Maui.Graphics.Colors.White : Microsoft.Maui.Graphics.Colors.Black;
                canvas.DrawString(valueLabels[i], left, y, width, LabelHeight, HorizontalAlignment.Center, VerticalAlignment.Center);
                valueTexts.Add(new RectF(left - 3, y + 2, width + 6, LabelHeight - 4));
            }

            if (Labels is { } names && i < names.Count)
            {
                var strong = selected >= 0 ? i == selected : HighlightLast && i == count - 1;
                canvas.Font = new Microsoft.Maui.Graphics.Font(FontFamily, strong ? FontWeights.Bold : FontWeights.Regular);
                canvas.FontSize = 11;
                canvas.FontColor = strong ? (dark ? Microsoft.Maui.Graphics.Colors.White : Microsoft.Maui.Graphics.Colors.Black) : dark ? Color.FromArgb("#8D8D93") : Color.FromArgb("#6D6D72");
                canvas.DrawString(names[i], slotLeft, top + height + 4, slotWidth, LabelHeight,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }

        if (along is not null)
            canvas.RestoreState();

        if (ReferenceValue is { } reference && max > 0)
        {
            // Over the columns, as a target over the values; its label at the trailing end, beside the line.
            var y = top + height - height * (float)Math.Clamp(reference / max, 0, 1);
            var muted = dark ? Color.FromArgb("#8D8D93") : Color.FromArgb("#86868B");
            canvas.StrokeColor = muted;
            canvas.StrokeSize = 1;
            canvas.StrokeDashPattern = [3, 3];
            // Broken where a value's text crosses it, so the line never runs through a number.
            var from = rect.X;
            foreach (var cut in valueTexts.Where(t => y >= t.Top && y <= t.Bottom).OrderBy(t => t.Left))
            {
                if (cut.Left > from)
                    canvas.DrawLine(from, y, cut.Left, y);
                from = Math.Max(from, cut.Right);
            }
            if (rect.Right - referenceRoom > from)
                canvas.DrawLine(from, y, rect.Right - referenceRoom, y);
            canvas.StrokeDashPattern = null;
            if (ReferenceLabel is { Length: > 0 } text)
            {
                canvas.Font = new Microsoft.Maui.Graphics.Font(FontFamily);
                canvas.FontSize = 11;
                canvas.FontColor = muted;
                canvas.DrawString(text, rect.Right - referenceRoom + 4, y - LabelHeight / 2, referenceRoom - 4, LabelHeight,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }
    }
}
