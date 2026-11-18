using System.Globalization;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Controls;

/// <summary>The point picked out on a <see cref="LineChart"/>: where it is across the chart, and its colour on the band.</summary>
public sealed record ChartSelection(ChartPoint Point, double X, Color Color);

/// <summary>
/// A band between a lower and an upper series, drawn under a <see cref="LineChart"/>'s line, such as the spread of a
/// day's values around their median. <see cref="Color"/> is the fill; the chart's colour, faint, when null.
/// </summary>
public sealed record ChartBand(IReadOnlyList<ChartRange> Ranges, Color? Color = null);

/// <summary>
/// A time series as a line over a band, such as glucose over its target range, drawn the way iOS draws its own
/// charts: a smooth line with round ends, a faint fade under it, quiet grid lines with the values on the trailing
/// side, and times on round hours of the wall clock, marked where the clock changes. A gap in the data breaks the
/// line. The last point is marked. Touching the chart picks out the nearest point for a moment; nothing stays
/// selected. <see cref="Bands"/> fill between two series under the line. With <see cref="ShowAxes"/> off it is a small
/// graph for a card. 230 points tall by default.
/// <para>
/// In a <see cref="ChartScroller"/> it is moved along time: the scroller sets <see cref="From"/>, <see cref="To"/>,
/// <see cref="Points"/> and <see cref="Bands"/>, and the line, its fade and the bands are cut off at the plot's edges.
/// </para>
/// </summary>
public sealed class LineChart : ChartView, IScrollableChart
{
    public static readonly BindableProperty PointsProperty = Create(nameof(Points), typeof(IList<ChartPoint>), null);
    public static readonly BindableProperty BandsProperty = Create(nameof(Bands), typeof(IList<ChartBand>), null);
    public static readonly BindableProperty FromProperty = Create(nameof(From), typeof(DateTimeOffset?), null);
    public static readonly BindableProperty ToProperty = Create(nameof(To), typeof(DateTimeOffset?), null);
    public static readonly BindableProperty TimeZoneProperty = Create(nameof(TimeZone), typeof(TimeZoneInfo), null);
    public static readonly BindableProperty MinimumProperty = Create(nameof(Minimum), typeof(double?), null);
    public static readonly BindableProperty MaximumProperty = Create(nameof(Maximum), typeof(double?), null);
    public static readonly BindableProperty BandLowProperty = Create(nameof(BandLow), typeof(double?), null);
    public static readonly BindableProperty BandHighProperty = Create(nameof(BandHigh), typeof(double?), null);
    public static readonly BindableProperty BandColorProperty = Create(nameof(BandColor), typeof(Color), null);
    public static readonly BindableProperty BandLabelProperty = Create(nameof(BandLabel), typeof(string), null);
    public static readonly BindableProperty GapLabelProperty = Create(nameof(GapLabel), typeof(string), null);
    public static readonly BindableProperty FadeColorProperty = Create(nameof(FadeColor), typeof(Color), null);
    public static readonly BindableProperty IsCurrentProperty = Create(nameof(IsCurrent), typeof(bool), true);
    public static readonly BindableProperty BelowColorProperty = Create(nameof(BelowColor), typeof(Color), null);
    public static readonly BindableProperty AboveColorProperty = Create(nameof(AboveColor), typeof(Color), null);
    public static readonly BindableProperty GapAfterProperty = Create(nameof(GapAfter), typeof(TimeSpan?), null);
    public static readonly BindableProperty MinimumGapProperty = Create(nameof(MinimumGap), typeof(TimeSpan?), null);
    public static readonly BindableProperty LongGapProperty = Create(nameof(LongGap), typeof(TimeSpan?), null);
    public static readonly BindableProperty ShowAxesProperty = Create(nameof(ShowAxes), typeof(bool), true);
    public static readonly BindableProperty FillProperty = Create(nameof(Fill), typeof(bool), true);
    public static readonly BindableProperty ShowBandProperty = Create(nameof(ShowBand), typeof(bool), true);
    public static readonly BindableProperty SmoothProperty = Create(nameof(Smooth), typeof(bool), true);
    public static readonly BindableProperty HighlightLastProperty = Create(nameof(HighlightLast), typeof(bool), true);
    public static readonly BindableProperty IsLiveProperty =
        BindableProperty.Create(nameof(IsLive), typeof(bool), typeof(LineChart), false, propertyChanged: static (b, _, _) => ((LineChart)b).UpdatePulse());
    public static readonly BindableProperty ValueFormatProperty = Create(nameof(ValueFormat), typeof(string), "0.#");
    public static readonly BindableProperty TimeFormatProperty = Create(nameof(TimeFormat), typeof(string), "HH:mm");
    public static readonly BindableProperty ClockBackLabelProperty = Create(nameof(ClockBackLabel), typeof(string), null);
    public static readonly BindableProperty ClockForwardLabelProperty = Create(nameof(ClockForwardLabel), typeof(string), null);
    public static readonly BindableProperty FontFamilyProperty = Create(nameof(FontFamily), typeof(string), null);

    static readonly BindablePropertyKey SelectedIndexPropertyKey =
        BindableProperty.CreateReadOnly(nameof(SelectedIndex), typeof(int), typeof(LineChart), -1, propertyChanged: Redraw);

    public static readonly BindableProperty SelectedIndexProperty = SelectedIndexPropertyKey.BindableProperty;

    static readonly BindablePropertyKey SelectionPropertyKey =
        BindableProperty.CreateReadOnly(nameof(Selection), typeof(ChartSelection), typeof(LineChart), null);

    public static readonly BindableProperty SelectionProperty = SelectionPropertyKey.BindableProperty;

    /// <summary>The values, in any order; they are drawn by time.</summary>
    public IList<ChartPoint>? Points { get => (IList<ChartPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    /// <summary>
    /// Bands between a lower and an upper series, drawn under the line in their order, each broken where its times pause
    /// as the line is. Their values are in the value axis. None when null.
    /// </summary>
    public IList<ChartBand>? Bands { get => (IList<ChartBand>?)GetValue(BandsProperty); set => SetValue(BandsProperty, value); }

    /// <summary>The left end of the time axis; the first point when null. Set it to show a fixed window, such as the last 3 hours.</summary>
    public DateTimeOffset? From { get => (DateTimeOffset?)GetValue(FromProperty); set => SetValue(FromProperty, value); }

    /// <summary>The right end of the time axis; the last point when null.</summary>
    public DateTimeOffset? To { get => (DateTimeOffset?)GetValue(ToProperty); set => SetValue(ToProperty, value); }

    /// <summary>The zone of the times on the axis and of the clock changes; the device's when null.</summary>
    public TimeZoneInfo? TimeZone { get => (TimeZoneInfo?)GetValue(TimeZoneProperty); set => SetValue(TimeZoneProperty, value); }

    /// <summary>The bottom of the value axis; fitted to the points, the bands and the band when null.</summary>
    public double? Minimum { get => (double?)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    /// <summary>The top of the value axis; fitted to the points, the bands and the band when null.</summary>
    public double? Maximum { get => (double?)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <summary>The bottom of the band, such as the low end of a target range; no band without both ends.</summary>
    public double? BandLow { get => (double?)GetValue(BandLowProperty); set => SetValue(BandLowProperty, value); }

    /// <summary>The top of the band.</summary>
    public double? BandHigh { get => (double?)GetValue(BandHighProperty); set => SetValue(BandHighProperty, value); }

    /// <summary>The band's fill; <see cref="ChartView.Color"/>, faint, when null.</summary>
    public Color? BandColor { get => (Color?)GetValue(BandColorProperty); set => SetValue(BandColorProperty, value); }

    /// <summary>
    /// A short text at the band's top edge saying what the band is, such as "Target 3.9–10.0", so the line's colours
    /// above and below it explain themselves, for a chart without a legend of its own. None when null.
    /// </summary>
    public string? BandLabel { get => (string?)GetValue(BandLabelProperty); set => SetValue(BandLabelProperty, value); }

    /// <summary>Said in a gap longer than <see cref="LongGap"/> on a chart with axes, such as "No readings". None when null.</summary>
    public string? GapLabel { get => (string?)GetValue(GapLabelProperty); set => SetValue(GapLabelProperty, value); }

    /// <summary>
    /// What the fade under the line fades into downwards: the colour behind the chart. Without it the fade has one
    /// strength from top to bottom.
    /// </summary>
    public Color? FadeColor { get => (Color?)GetValue(FadeColorProperty); set => SetValue(FadeColorProperty, value); }

    /// <summary>
    /// Whether the last point is the current value. When it is not (a sensor lost its signal, the value is old), the
    /// last point is drawn hollow, without its halo, and a dashed line runs on from it to <see cref="To"/>, so the
    /// missing time shows as missing. True by default.
    /// </summary>
    public bool IsCurrent { get => (bool)GetValue(IsCurrentProperty); set => SetValue(IsCurrentProperty, value); }

    /// <summary>The line below the band, such as orange-red for low; <see cref="ChartView.Color"/> when null.</summary>
    public Color? BelowColor { get => (Color?)GetValue(BelowColorProperty); set => SetValue(BelowColorProperty, value); }

    /// <summary>The line above the band; <see cref="ChartView.Color"/> when null.</summary>
    public Color? AboveColor { get => (Color?)GetValue(AboveColorProperty); set => SetValue(AboveColorProperty, value); }

    /// <summary>A pause between two points longer than this breaks the line; three times the usual spacing when null.</summary>
    public TimeSpan? GapAfter { get => (TimeSpan?)GetValue(GapAfterProperty); set => SetValue(GapAfterProperty, value); }

    /// <summary>
    /// The shortest pause that breaks the line when <see cref="GapAfter"/> is null, for a series whose spacing changes,
    /// such as minute values after a history of 5-minute ones. None when null.
    /// </summary>
    public TimeSpan? MinimumGap { get => (TimeSpan?)GetValue(MinimumGapProperty); set => SetValue(MinimumGapProperty, value); }

    /// <summary>
    /// A gap up to this long is a short one, crossed by a dotted line; a longer one breaks the band and says
    /// <see cref="GapLabel"/>. Twice the pause that breaks the line when null.
    /// </summary>
    public TimeSpan? LongGap { get => (TimeSpan?)GetValue(LongGapProperty); set => SetValue(LongGapProperty, value); }

    /// <summary>Values on the trailing side and times underneath, with grid lines. Off for a small graph. True by default.</summary>
    public bool ShowAxes { get => (bool)GetValue(ShowAxesProperty); set => SetValue(ShowAxesProperty, value); }

    /// <summary>A fade under the line. True by default.</summary>
    public bool Fill { get => (bool)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    /// <summary>
    /// Whether the band is drawn. Off for a small graph, where colour over the line reads as data: the line's colours
    /// above, inside and below the band still follow it. True by default.
    /// </summary>
    public bool ShowBand { get => (bool)GetValue(ShowBandProperty); set => SetValue(ShowBandProperty, value); }

    /// <summary>
    /// A smooth line instead of straight segments: eased a few points wide, so whole-number steps in the data do not
    /// show as a staircase, and rounded at peaks and troughs. Only the line is smoothed (the bands are rounded, not
    /// eased); the last point, values outside the band and a point picked out keep their values. True by default.
    /// </summary>
    public bool Smooth { get => (bool)GetValue(SmoothProperty); set => SetValue(SmoothProperty, value); }

    /// <summary>Whether the last point is marked, as the current reading. True by default.</summary>
    public bool HighlightLast { get => (bool)GetValue(HighlightLastProperty); set => SetValue(HighlightLastProperty, value); }

    /// <summary>
    /// Whether the last point is live: a thin ring then grows from it and fades, every couple of seconds. Bind it to the
    /// value's freshness, so an old value stops pulsing. Still when the system asks for reduced motion. False by default.
    /// </summary>
    public bool IsLive { get => (bool)GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }

    /// <summary>The format of the values on the axis and in the tooltip, in the current culture. <c>0.#</c> by default.</summary>
    public string ValueFormat { get => (string)GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }

    /// <summary>The format of the times, on <see cref="TimeZone"/>'s clock. <c>HH:mm</c> by default.</summary>
    public string TimeFormat { get => (string)GetValue(TimeFormatProperty); set => SetValue(TimeFormatProperty, value); }

    /// <summary>Said where the clock went back an hour; Spine's "Winter time" (<c>Spine.Charts.ClockBack</c>) when null.</summary>
    public string? ClockBackLabel { get => (string?)GetValue(ClockBackLabelProperty); set => SetValue(ClockBackLabelProperty, value); }

    /// <summary>Said where the clock went forward; Spine's "Summer time" (<c>Spine.Charts.ClockForward</c>) when null.</summary>
    public string? ClockForwardLabel { get => (string?)GetValue(ClockForwardLabelProperty); set => SetValue(ClockForwardLabelProperty, value); }

    /// <summary>The labels' font, or null for the platform's.</summary>
    public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    /// <summary>The index in time order of the point picked out, or -1.</summary>
    public int SelectedIndex => (int)GetValue(SelectedIndexProperty);

    /// <summary>
    /// The point picked out, or null. The chart draws the guide line and the dot; what the point says is up to the
    /// page, as real text over the top of the plot for the moment it shows, so it can be set like any label and the
    /// chart keeps no empty row for it.
    /// </summary>
    public ChartSelection? Selection => (ChartSelection?)GetValue(SelectionProperty);

    /// <summary>How far down the chart the plot's top line is drawn, the value axis's highest value: where a page lines up its label.</summary>
    public double PlotTop => Inset;

    const float MinFillWidth = 24;
    const float AxisWidth = 28, AxisHeight = 22, LabelSize = 11, Inset = 7, Dot = 3.5f, Halo = 6.5f;

    /// <summary>Points closer than this in points are merged before drawing, so a dense series reads as a calm line.</summary>
    const float MinSpacing = 3;

    /// <summary>
    /// How wide the smoothing of the drawn line is: at least this many points on screen, and wider where the points
    /// lie further apart (<see cref="SmoothSpacings"/> of the usual spacing), so sparse points over a day are as calm
    /// as dense points over an hour.
    /// </summary>
    const float SmoothWidth = 6;
    const float SmoothSpacings = 1.6f;

    static readonly TimeSpan SelectionLingers = TimeSpan.FromSeconds(3);
    static readonly TimeSpan[] TimeSteps = [.. new[] { 5, 10, 15, 30, 60, 120, 180, 240, 360, 720 }.Select(m => TimeSpan.FromMinutes(m))];

    ChartPoint[] _shown = [];
    RectF _plot;
    bool _scrolled, _dragSelects = true;
    DateTimeOffset _from, _to;
    CancellationTokenSource? _linger;

    const string PulseAnimation = "LineChartPulse";
    static readonly TimeSpan PulsePeriod = TimeSpan.FromSeconds(2.2);

    /// <summary>Where the pulse is in its period, 0 to 1.</summary>
    float _pulse;

    static LineChart() => SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(LineChart).Assembly));

    public LineChart()
    {
        HeightRequest = 230;
        Loaded += (_, _) => UpdatePulse();
        Unloaded += (_, _) => this.AbortAnimation(PulseAnimation);
        var pointer = new PointerGestureRecognizer();
        // On a phone a finger moves the pointer too, and never leaves: it lets go by itself, as a tap does.
        pointer.PointerMoved += (_, e) =>
        {
            if (!_dragSelects)
                return;
            if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
                Select(e.GetPosition(this) is { } p ? IndexAt(p.X) : -1);
            else
                Touch(e.GetPosition(this)?.X);
        };
        pointer.PointerExited += (_, _) => Select(-1);
        GestureRecognizers.Add(pointer);
        // Touch has no pointer to rest: a tap picks the nearest point out, and it lets go by itself.
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) => Touch(e.GetPosition(this)?.X);
        GestureRecognizers.Add(tap);
    }

    static BindableProperty Create(string name, Type type, object? defaultValue) =>
        BindableProperty.Create(name, type, typeof(LineChart), defaultValue, propertyChanged: Redraw);

    TimeZoneInfo Zone => TimeZone ?? TimeZoneInfo.Local;

    void UpdatePulse()
    {
        this.AbortAnimation(PulseAnimation);
        _pulse = 0;
        if (IsLive && IsLoaded && !ReducedMotion.IsOn)
        {
            new Animation(v => { _pulse = (float)v; Invalidate(); }, 0, 1)
                .Commit(this, PulseAnimation, rate: 33, length: (uint)PulsePeriod.TotalMilliseconds, easing: Easing.Linear, repeat: () => IsLive);
        }
        Invalidate();
    }

    void Touch(double? x)
    {
        if (x is null)
            return;
        Select(IndexAt(x.Value));
        _linger?.Cancel();
        _linger = new CancellationTokenSource();
        var token = _linger.Token;
        Dispatcher.DispatchDelayed(SelectionLingers, () =>
        {
            if (!token.IsCancellationRequested)
                Select(-1);
        });
    }

    /// <summary>New points or another range: a picked point is an index into the old ones, so it lets go.</summary>
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName is nameof(Points) or nameof(From) or nameof(To))
            Select(-1);
    }

    int IndexAt(double x)
    {
        if (_shown.Length == 0 || _plot.Width <= 0)
            return -1;
        var best = -1;
        var distance = double.MaxValue;
        for (var i = 0; i < _shown.Length; i++)
        {
            // In a scroller the history reaches past the window: only a point in view can be picked out.
            if (_scrolled && (_shown[i].Time < _from || _shown[i].Time > _to))
                continue;
            var d = Math.Abs(XOf(_shown[i].Time) - x);
            if (d < distance)
            {
                distance = d;
                best = i;
            }
        }
        return best;
    }

    void Select(int index)
    {
        if (!ShowAxes || index == SelectedIndex)
            return;
        if (index >= 0)
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        SetValue(SelectedIndexPropertyKey, index);
        SetValue(SelectionPropertyKey, index < 0 ? null : new ChartSelection(_shown[index], XOf(_shown[index].Time), ColorOf(_shown[index].Value)));
        ToolTipProperties.SetText(this, index < 0 ? null! : Describe(_shown[index]));
    }

    string Describe(ChartPoint point) =>
        $"{point.Value.ToString(ValueFormat, CultureInfo.CurrentCulture)} · {TimeZoneInfo.ConvertTime(point.Time, Zone).ToString(TimeFormat, CultureInfo.CurrentCulture)}";

    float XOf(DateTimeOffset time) =>
        _plot.Left + (float)((time - _from).TotalSeconds / Math.Max(1, (_to - _from).TotalSeconds)) * _plot.Width;

    public override void Draw(ICanvas canvas, RectF rect)
    {
        var sorted = Points is { Count: > 0 } points ? points.OrderBy(p => p.Time).ToArray() : [];
        var bands = Bands?.Where(b => b.Ranges.Count > 0).Select(b => (Band: b, Ranges: b.Ranges.OrderBy(r => r.Time).ToArray())).ToList() ?? [];
        var times = sorted.Select(p => p.Time).Concat(bands.SelectMany(b => b.Ranges.Select(r => r.Time))).ToList();
        if (times.Count == 0 && (From is null || To is null))
            return;

        _from = From ?? times.Min();
        _to = To ?? times.Max();
        if (_to <= _from)
            _to = _from.AddMinutes(1);

        var axes = ShowAxes;
        // Room for the last point's halo at the right and at both edges, so the line starts and ends the same way.
        _plot = new RectF(
            rect.X + Inset,
            rect.Y + Inset,
            rect.Width - Inset * 2 - (axes ? AxisWidth : 0),
            rect.Height - Inset * 2 - (axes ? AxisHeight : 0));

        var gap = LineSeries.GapThreshold([.. sorted.Select(p => p.Time)], GapAfter, MinimumGap);
        var longGap = LongGap ?? gap * 2;
        var bucketSeconds = _plot.Width > 0 ? (_to - _from).TotalSeconds * MinSpacing / _plot.Width : 0;
        _shown = LineSeries.Thin(sorted, bucketSeconds, gap, BandLow, BandHigh);

        // A drawn band is in view; without one the axis fits the line, so a small graph shows its shape.
        var values = _shown.Select(p => p.Value).Concat(bands.SelectMany(b => b.Ranges.SelectMany(r => new[] { r.Low, r.High }))).ToList();
        if (ShowBand && BandLow is { } bandLow) values.Add(bandLow);
        if (ShowBand && BandHigh is { } bandHigh) values.Add(bandHigh);
        var (min, max, step) = LineSeries.ValueAxis(values, Minimum, Maximum, axes);
        float Y(double value) => _plot.Bottom - (float)((value - min) / (max - min)) * _plot.Height;

        var dark = Application.Current?.RequestedTheme != AppTheme.Light;
        var muted = dark ? Color.FromArgb("#8D8D93") : Color.FromArgb("#86868B");
        var font = new Microsoft.Maui.Graphics.Font(FontFamily);

        if (axes)
            DrawAxes(canvas, min, max, step, Y, dark, muted, font);

        // Thinning spaces the points wider than the data: a gap is only one wider than that, too.
        var segments = LineSeries.Split(_shown, p => p.Time, gap > TimeSpan.FromSeconds(bucketSeconds * 2) ? gap : TimeSpan.FromSeconds(bucketSeconds * 2));
        var gaps = new List<(PointF From, PointF To, bool Long)>();
        for (var i = 1; i < segments.Count; i++)
        {
            var (a, b) = (segments[i - 1][^1], segments[i][0]);
            gaps.Add((new PointF(XOf(a.Time), Y(a.Value)), new PointF(XOf(b.Time), Y(b.Value)), b.Time - a.Time > longGap));
        }

        // In a scroller the history runs past the window: what is drawn along time stops at the plot's edges.
        if (_scrolled)
        {
            canvas.SaveState();
            canvas.ClipRectangle(_plot.Left, rect.Top, _plot.Width, rect.Height);
        }

        if (ShowBand && BandLow is { } low && BandHigh is { } high && high > low)
        {
            // The band breaks where the data does, for a gap long enough to name.
            var top = Y(Math.Min(high, max));
            var bottom = Y(Math.Max(low, min));
            canvas.FillColor = BandColor ?? Color.WithAlpha(dark ? 0.14f : 0.09f);
            var left = _plot.Left;
            foreach (var g in gaps.Where(g => g.Long))
            {
                if (g.From.X > left)
                    canvas.FillRoundedRectangle(left, top, g.From.X - left, bottom - top, 4);
                left = g.To.X;
            }
            if (_plot.Right > left)
                canvas.FillRoundedRectangle(left, top, _plot.Right - left, bottom - top, 4);
        }

        foreach (var (band, ranges) in bands)
            DrawBand(canvas, band, ranges, Y, dark);

        // A short gap is crossed by a dotted line; a long one, wide enough, says so.
        foreach (var g in gaps)
        {
            if (!g.Long)
            {
                canvas.StrokeColor = muted.WithAlpha(0.8f);
                canvas.StrokeSize = 1.5f;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.StrokeDashPattern = [0.5f, 3];
                canvas.DrawLine(g.From, g.To);
                canvas.StrokeDashPattern = null;
            }
            else if (axes && GapLabel is { Length: > 0 } gapLabel && g.To.X - g.From.X >= canvas.GetStringSize(gapLabel, font, LabelSize).Width + 12)
            {
                canvas.Font = font;
                canvas.FontSize = LabelSize;
                canvas.FontColor = muted;
                canvas.DrawString(gapLabel, g.From.X, _plot.Center.Y - LabelSize, g.To.X - g.From.X, LabelSize * 2,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }

        var stroke = axes ? 2.5f : 2f;
        var bandTop = BandHigh is { } h ? Y(Math.Min(h, max)) : _plot.Top;
        var bandBottom = BandLow is { } l ? Y(Math.Max(l, min)) : _plot.Bottom;
        if (Fill)
        {
            // A stretch only a few points wide (just after a gap) gets no fade: under it the fade is a bar, not an area.
            foreach (var segment in segments.Where(s => s.Length > 1 && XOf(s[^1].Time) - XOf(s[0].Time) >= MinFillWidth))
            {
                var pixels = Pixels(segment, Y);
                // A small graph has no axis to end on: its fill runs past the ends of the line and fades out there.
                var past = axes ? 0 : Inset;
                var area = Curve(pixels);
                area.LineTo(pixels[^1].X + past, pixels[^1].Y);
                area.LineTo(pixels[^1].X + past, _plot.Bottom);
                area.LineTo(pixels[0].X - past, _plot.Bottom);
                area.LineTo(pixels[0].X - past, pixels[0].Y);
                area.Close();
                FillUnder(canvas, area, pixels, past, bandTop, bandBottom);
            }
        }

        canvas.StrokeSize = stroke;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.Antialias = true;
        // The line in up to three colours: clipped above, inside and below the band.
        foreach (var (color, from, to) in new[]
        {
            (AboveColor ?? Color, rect.Top, bandTop),
            (Color, bandTop, bandBottom),
            (BelowColor ?? Color, bandBottom, rect.Bottom),
        })
        {
            if (to <= from)
                continue;
            canvas.SaveState();
            canvas.ClipRectangle(rect.X, from, rect.Width, to - from);
            canvas.StrokeColor = color;
            foreach (var segment in segments)
            {
                if (segment.Length == 1)
                {
                    canvas.FillColor = color;
                    canvas.FillCircle(XOf(segment[0].Time), Y(segment[0].Value), stroke);
                    continue;
                }
                canvas.DrawPath(Curve(Pixels(segment, Y)));
            }
            canvas.RestoreState();
        }
        if (_scrolled)
            canvas.RestoreState();

        // The band's label goes over the line, so a line passing through it never hides it.
        if (axes && ShowBand && BandLabel is { Length: > 0 } label && BandHigh is { } labelHigh)
        {
            canvas.Font = new Microsoft.Maui.Graphics.Font(FontFamily, FontWeights.SemiBold);
            canvas.FontSize = LabelSize;
            canvas.FontColor = Color.WithLuminosity(dark ? 0.7f : 0.3f);
            canvas.DrawString(label, _plot.Left + 8, Y(Math.Min(labelHigh, max)) + 3, _plot.Width - 16, LabelSize + 4, HorizontalAlignment.Left, VerticalAlignment.Top);
        }

        if (HighlightLast && _shown.Length > 0 && (!_scrolled || (_shown[^1].Time >= _from && _shown[^1].Time <= _to)))
            DrawLast(canvas, rect, Y, dark, muted);

        if (SelectedIndex >= 0 && SelectedIndex < _shown.Length)
        {
            var point = _shown[SelectedIndex];
            var (x, y) = (XOf(point.Time), Y(point.Value));
            // From the top, where the page's label sits, so the label and the point read as one.
            canvas.StrokeColor = dark ? Color.FromArgb("#48484A") : Color.FromArgb("#D1D1D6");
            canvas.StrokeSize = 2;
            canvas.DrawLine(x, rect.Y, x, _plot.Bottom);
            canvas.FillColor = dark ? Colors.Black : Colors.White;
            canvas.FillCircle(x, y, Dot + 2);
            canvas.FillColor = ColorOf(point.Value);
            canvas.FillCircle(x, y, Dot);
        }
    }

    void IScrollableChart.ShowWindow(DateTimeOffset from, DateTimeOffset to)
    {
        _scrolled = true;
        From = from;
        To = to;
    }

    void IScrollableChart.ShowHistory(ChartSlice history)
    {
        _scrolled = true;
        Points = history.Points is { Count: > 0 } points ? [.. points] : null;
        Bands = history.Bands is { Count: > 0 } bands ? [.. bands] : null;
    }

    bool IScrollableChart.SelectsByDragging { get => _dragSelects; set => _dragSelects = value; }

    void IScrollableChart.ClearSelection() => Select(-1);

    Thickness IScrollableChart.PlotMargin => new(Inset, 0, Inset + (ShowAxes ? AxisWidth : 0), 0);

    /// <summary>
    /// Drawn as Apple Health draws its charts: solid value lines with the values outside on the trailing side, a solid
    /// edge at the start and along the bottom, and a dashed line down through the time labels at each round time, the
    /// label just after it; and where the clock was set back or forward, a line through the plot and the change.
    /// </summary>
    void DrawAxes(ICanvas canvas, double min, double max, double step, Func<double, float> y, bool dark, Color muted, Microsoft.Maui.Graphics.Font font)
    {
        canvas.Font = font;
        canvas.FontSize = LabelSize;
        canvas.FontColor = muted;
        canvas.StrokeColor = dark ? Color.FromArgb("#3A3A3C") : Color.FromArgb("#E5E5EA");
        canvas.StrokeSize = 1;
        for (var value = min; value <= max + step / 2; value += step)
        {
            var at = y(value);
            canvas.DrawLine(_plot.Left, at, _plot.Right, at);
            canvas.DrawString(value.ToString(ValueFormat, CultureInfo.CurrentCulture), _plot.Right + 6, at - LabelSize, AxisWidth - 6, LabelSize * 2,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
        canvas.DrawLine(_plot.Left, _plot.Top, _plot.Left, _plot.Bottom);

        // Round times on the wall clock, so the labels stay right across a change between summer and winter time.
        var timeStep = TimeSteps.FirstOrDefault(s => (_to - _from) / s <= 4, TimeSpan.FromHours(12));
        canvas.StrokeDashPattern = [2, 2];
        foreach (var (at, wall) in LocalClock.Ticks(_from, _to, timeStep, Zone))
        {
            var x = XOf(at);
            canvas.DrawLine(x, _plot.Top, x, _plot.Bottom + AxisHeight);
            canvas.DrawString(wall.ToString(TimeFormat, CultureInfo.CurrentCulture), x + 4, _plot.Bottom + 4, 60, AxisHeight - 4,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        canvas.StrokeColor = muted;
        foreach (var (at, change) in LocalClock.Changes(_from, _to, Zone))
        {
            var x = XOf(at);
            canvas.DrawLine(x, _plot.Top, x, _plot.Bottom);
            var season = change < TimeSpan.Zero
                ? ClockBackLabel ?? SpineStrings.Current["Spine.Charts.ClockBack"]
                : ClockForwardLabel ?? SpineStrings.Current["Spine.Charts.ClockForward"];
            canvas.DrawString($"{season} {(change < TimeSpan.Zero ? "−" : "+")}{Math.Abs(change.TotalHours):0.#} h", x + 4, _plot.Top + 2, 140, LabelSize * 1.6f,
                HorizontalAlignment.Left, VerticalAlignment.Top);
        }
        canvas.StrokeDashPattern = null;
    }

    /// <summary>A band from its upper series and back along its lower one, broken where its times pause, rounded when <see cref="Smooth"/>.</summary>
    void DrawBand(ICanvas canvas, ChartBand band, ChartRange[] ranges, Func<double, float> y, bool dark)
    {
        canvas.FillColor = band.Color ?? Color.WithAlpha(dark ? 0.22f : 0.16f);
        var gap = LineSeries.GapThreshold([.. ranges.Select(r => r.Time)], GapAfter, MinimumGap);
        foreach (var run in LineSeries.Split(ranges, r => r.Time, gap).Where(r => r.Length > 1))
        {
            var upper = run.Select(r => (XOf(r.Time), y(r.High))).ToArray();
            var lower = run.Reverse().Select(r => (XOf(r.Time), y(r.Low))).ToArray();
            var path = Curve(upper);
            path.LineTo(lower[0].Item1, lower[0].Item2);
            AppendCurve(path, lower);
            path.Close();
            canvas.FillPath(path);
        }
    }

    /// <summary>The last point: filled with a halo when current, hollow with a dashed line on to <see cref="To"/> when not, and the pulse.</summary>
    void DrawLast(ICanvas canvas, RectF rect, Func<double, float> y0, bool dark, Color muted)
    {
        var last = _shown[^1];
        var color = ColorOf(last.Value);
        var (x, y) = (XOf(last.Time), y0(last.Value));
        if (!IsCurrent)
        {
            if (XOf(_to) - x > Dot * 2)
            {
                canvas.StrokeColor = muted.WithAlpha(0.8f);
                canvas.StrokeSize = 1.5f;
                canvas.StrokeDashPattern = [3, 3];
                canvas.DrawLine(x + Dot, y, XOf(_to), y);
                canvas.StrokeDashPattern = null;
            }
            canvas.FillColor = FadeColor ?? (dark ? Colors.Black : Colors.White);
            canvas.FillCircle(x, y, Dot);
            canvas.StrokeColor = color;
            canvas.StrokeSize = 1.5f;
            canvas.DrawCircle(x, y, Dot);
        }
        else
        {
            canvas.FillColor = color.WithAlpha(0.22f);
            canvas.FillCircle(x, y, Halo);
            canvas.FillColor = color;
            canvas.FillCircle(x, y, Dot);
        }

        // The pulse: a ring that grows out of the halo and fades in the first 60 % of the period, then rests.
        if (IsLive && IsCurrent && _pulse is > 0 and < 0.6f)
        {
            var t = _pulse / 0.6f;
            var eased = 1 - (1 - t) * (1 - t);
            var reach = Math.Min(Halo + 3, Math.Min(rect.Right - x, Math.Min(y - rect.Top, rect.Bottom - y)));
            canvas.StrokeColor = color.WithAlpha(0.5f * (1 - t));
            canvas.StrokeSize = 1.5f;
            canvas.DrawCircle(x, y, Dot + (reach - Dot) * eased);
        }
    }

    Color ColorOf(double value) =>
        BandLow is { } low && value < low ? BelowColor ?? Color : BandHigh is { } high && value > high ? AboveColor ?? Color : Color;

    /// <summary>
    /// A segment's points on screen: as they are, or with <see cref="Smooth"/> eased by a local fit a few points wide
    /// (wider where the points lie further apart), the last one and those outside the band as measured.
    /// </summary>
    (float X, float Y)[] Pixels(ChartPoint[] segment, Func<double, float> y)
    {
        var p = segment.Select(point => (XOf(point.Time), y(point.Value))).ToArray();
        if (!Smooth || p.Length < 3)
            return p;
        var spacings = p.Zip(p.Skip(1), (a, b) => b.Item1 - a.Item1).Order().ToArray();
        var width = Math.Max(SmoothWidth, spacings[spacings.Length / 2] * SmoothSpacings);
        return ChartSmoothing.Gaussian(p, width, i => segment[i].Value < BandLow || segment[i].Value > BandHigh);
    }

    /// <summary>The line through the points: straight, or with <see cref="Smooth"/> a Catmull-Rom curve.</summary>
    PathF Curve((float X, float Y)[] p)
    {
        var path = new PathF(p[0].X, p[0].Y);
        AppendCurve(path, p);
        return path;
    }

    void AppendCurve(PathF path, (float X, float Y)[] p)
    {
        if (!Smooth || p.Length < 3)
        {
            foreach (var (x, py) in p.Skip(1))
                path.LineTo(x, py);
            return;
        }
        foreach (var (c1, c2, end) in ChartSmoothing.CatmullRom(p))
            path.CurveTo(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
    }

    /// <summary>
    /// The fade under the line in the colour of the line above it: the colour changes along the line where it leaves or
    /// enters the band, blended over a few points so the changes are soft. It fades to nothing downwards and, over
    /// <paramref name="past"/> each side of the ends, sideways, so it sits on any background.
    /// </summary>
    /// <remarks>
    /// The canvas has no masks, so the downward fade is made of thin strips, each with the sideways gradient at its own
    /// strength; at this alpha the steps cannot be seen, as long as the strips meet on whole pixels.
    /// </remarks>
    void FillUnder(ICanvas canvas, PathF area, (float X, float Y)[] line, float past, float bandTop, float bandBottom)
    {
        const int Strips = 24;
        const int Samples = 48;
        const int Blend = 3;
        var (left, right) = (line[0].X, line[^1].X);
        var colours = new Color[Samples + 1];
        var j = 0;
        for (var i = 0; i <= Samples; i++)
        {
            var x = left + (right - left) * i / Samples;
            while (j < line.Length - 2 && line[j + 1].X < x)
                j++;
            var (a, b) = (line[j], line[Math.Min(j + 1, line.Length - 1)]);
            var y = b.X > a.X ? a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X) : a.Y;
            colours[i] = y < bandTop ? AboveColor ?? Color : y > bandBottom ? BelowColor ?? Color : Color;
        }
        var blended = new Color[Samples + 1];
        for (var i = 0; i <= Samples; i++)
        {
            var near = colours[Math.Max(0, i - Blend)..(Math.Min(Samples, i + Blend) + 1)];
            blended[i] = new Color(near.Average(c => c.Red), near.Average(c => c.Green), near.Average(c => c.Blue));
        }

        // Sideways: full from a little inside each end to nothing past it.
        var (from, to) = (left - past, right + past);
        var width = Math.Max(1, to - from);
        var ramp = past > 0 ? Math.Min(past * 1.6f, (right - left) / 2 + past) / width : 0;
        float Side(float at) => ramp <= 0 ? 1 : Math.Clamp(Math.Min(at, 1 - at) / ramp, 0, 1);

        var top = line.Min(q => q.Y);
        var height = Math.Max(1, _plot.Bottom - top);
        // Strips that meet on whole pixels, neither overlapping nor apart: a seam drawn twice, or not at all, shows as a line.
        var density = (float)Math.Max(1, DeviceDisplay.Current.MainDisplayInfo.Density);
        float Pixel(float y) => MathF.Round(y * density) / density;
        for (var k = 0; k < Strips; k++)
        {
            var (stripTop, stripBottom) = (Pixel(top + height * k / Strips), Pixel(top + height * (k + 1) / Strips));
            if (stripBottom <= stripTop)
                continue;
            var strength = 0.24f * (1 - (k + 0.5f) / Strips);
            var stops = new PaintGradientStop[Samples + 1];
            for (var i = 0; i <= Samples; i++)
            {
                var x = left + (right - left) * i / Samples;
                var at = (x - from) / width;
                stops[i] = new PaintGradientStop(at, blended[i].WithAlpha(strength * Side(at)));
            }
            canvas.SaveState();
            canvas.ClipRectangle(from, stripTop, width, stripBottom - stripTop);
            canvas.SetFillPaint(new LinearGradientPaint([new PaintGradientStop(0, blended[0].WithAlpha(0)), .. stops, new PaintGradientStop(1, blended[^1].WithAlpha(0))],
                new Point(0, 0.5), new Point(1, 0.5)), new RectF(from, _plot.Top, width, _plot.Height));
            canvas.FillPath(area);
            canvas.RestoreState();
        }
    }
}
