using System.Diagnostics;
using System.Globalization;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A chart with a time axis, moved along time to see its history: <see cref="Span"/> of time across the plot, ending at
/// <see cref="To"/>, between <see cref="Earliest"/> and <see cref="Latest"/>. Two fingers drag it, and a flick glides
/// and comes to rest on a round time (<see cref="SnapTo"/>); one finger picks a value out as the chart does on its
/// own, or scrolls too with <see cref="OneFinger"/> set to <see cref="ChartDrag.Scroll"/>. With a mouse a drag scrolls
/// and pointing picks a value out.
/// <para>
/// The history comes from <see cref="Source"/> in whole local days as the window nears them, and the days far from it
/// are let go; a day still loading has a soft pulse over it. The names of the days (or months) stand over the plot
/// and follow the scrolling: the one at the leading edge stays until the next pushes it out.
/// </para>
/// <para>
/// The chart is the content: a <see cref="LineChart"/> or a <see cref="ColumnChart"/>, or any view that is an
/// <see cref="IScrollableChart"/>. Its value axis stays still while the time moves, so give it a fixed scale
/// (<c>Minimum</c>/<c>Maximum</c>) where loading more days should not change it.
/// </para>
/// </summary>
[ContentProperty(nameof(Chart))]
public sealed class ChartScroller : ContentView
{
    public static readonly BindableProperty ChartProperty = BindableProperty.Create(nameof(Chart), typeof(View), typeof(ChartScroller), null,
        validateValue: static (_, value) => value is null or IScrollableChart,
        propertyChanged: static (b, o, n) => ((ChartScroller)b).OnChartChanged((View?)o, (View?)n));

    public static readonly BindableProperty SourceProperty = BindableProperty.Create(nameof(Source), typeof(IChartHistory), typeof(ChartScroller), null,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).Reset());

    public static readonly BindableProperty SpanProperty = BindableProperty.Create(nameof(Span), typeof(TimeSpan), typeof(ChartScroller), TimeSpan.FromDays(1),
        validateValue: static (_, value) => (TimeSpan)value > TimeSpan.Zero,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).Replace());

    public static readonly BindableProperty ToProperty = BindableProperty.Create(nameof(To), typeof(DateTimeOffset?), typeof(ChartScroller), null, BindingMode.TwoWay,
        propertyChanged: static (b, _, n) => ((ChartScroller)b).OnToChanged((DateTimeOffset?)n));

    public static readonly BindableProperty EarliestProperty = BindableProperty.Create(nameof(Earliest), typeof(DateTimeOffset?), typeof(ChartScroller), null,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).Replace());

    public static readonly BindableProperty LatestProperty = BindableProperty.Create(nameof(Latest), typeof(DateTimeOffset?), typeof(ChartScroller), null,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).Replace());

    public static readonly BindableProperty SnapToProperty = BindableProperty.Create(nameof(SnapTo), typeof(TimeSpan?), typeof(ChartScroller), null);

    public static readonly BindableProperty TimeZoneProperty = BindableProperty.Create(nameof(TimeZone), typeof(TimeZoneInfo), typeof(ChartScroller), null,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).Reset());

    public static readonly BindableProperty ChunkDaysProperty = BindableProperty.Create(nameof(ChunkDays), typeof(int), typeof(ChartScroller), 1,
        validateValue: static (_, value) => (int)value >= 1,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).Reset());

    public static readonly BindableProperty SectionsProperty = BindableProperty.Create(nameof(Sections), typeof(ChartSections), typeof(ChartScroller), ChartSections.Days,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).OnSectionsChanged());

    public static readonly BindableProperty SectionFormatProperty = BindableProperty.Create(nameof(SectionFormat), typeof(string), typeof(ChartScroller), null,
        propertyChanged: static (b, _, _) => ((ChartScroller)b)._sections.Invalidate());

    public static readonly BindableProperty ShortSectionFormatProperty = BindableProperty.Create(nameof(ShortSectionFormat), typeof(string), typeof(ChartScroller), null,
        propertyChanged: static (b, _, _) => ((ChartScroller)b)._sections.Invalidate());

    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(ChartScroller), null,
        propertyChanged: static (b, _, _) => ((ChartScroller)b)._sections.Invalidate());

    public static readonly BindableProperty OneFingerProperty = BindableProperty.Create(nameof(OneFinger), typeof(ChartDrag), typeof(ChartScroller), ChartDrag.Select,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).Recognize());

    static readonly BindablePropertyKey FromPropertyKey = BindableProperty.CreateReadOnly(nameof(From), typeof(DateTimeOffset?), typeof(ChartScroller), null);
    public static readonly BindableProperty FromProperty = FromPropertyKey.BindableProperty;

    static readonly BindablePropertyKey IsAtLatestPropertyKey = BindableProperty.CreateReadOnly(nameof(IsAtLatest), typeof(bool), typeof(ChartScroller), true);
    public static readonly BindableProperty IsAtLatestProperty = IsAtLatestPropertyKey.BindableProperty;

    static readonly BindablePropertyKey IsLoadingPropertyKey = BindableProperty.CreateReadOnly(nameof(IsLoading), typeof(bool), typeof(ChartScroller), false);
    public static readonly BindableProperty IsLoadingProperty = IsLoadingPropertyKey.BindableProperty;

    /// <summary>The chart moved along time: a <see cref="LineChart"/>, a <see cref="ColumnChart"/> or another <see cref="IScrollableChart"/>.</summary>
    public View? Chart { get => (View?)GetValue(ChartProperty); set => SetValue(ChartProperty, value); }

    /// <summary>Where the history comes from, a stretch of whole local days at a time. Without one the chart shows what it was given.</summary>
    public IChartHistory? Source { get => (IChartHistory?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>How much time the plot shows at once. A day by default.</summary>
    public TimeSpan Span { get => (TimeSpan)GetValue(SpanProperty); set => SetValue(SpanProperty, value); }

    /// <summary>
    /// The end of the time in view; <see cref="Latest"/> when null. Set when a scroll comes to rest, and setting it moves
    /// the chart there at once (<see cref="ScrollToAsync"/> glides).
    /// </summary>
    public DateTimeOffset? To { get => (DateTimeOffset?)GetValue(ToProperty); set => SetValue(ToProperty, value); }

    /// <summary>The start of the time in view, <see cref="To"/> less <see cref="Span"/>, when the chart has a size.</summary>
    public DateTimeOffset? From => (DateTimeOffset?)GetValue(FromProperty);

    /// <summary>How far back the history reaches; no end when null.</summary>
    public DateTimeOffset? Earliest { get => (DateTimeOffset?)GetValue(EarliestProperty); set => SetValue(EarliestProperty, value); }

    /// <summary>How far forward the history reaches; now when null, which moves on by itself.</summary>
    public DateTimeOffset? Latest { get => (DateTimeOffset?)GetValue(LatestProperty); set => SetValue(LatestProperty, value); }

    /// <summary>
    /// The round times a scroll comes to rest on, at the start of the window: on the clock of <see cref="TimeZone"/>, so a
    /// day rests on local midnight and an hour on a round hour across a change of clock. Anywhere when null. An end of
    /// the history is always a resting place.
    /// </summary>
    public TimeSpan? SnapTo { get => (TimeSpan?)GetValue(SnapToProperty); set => SetValue(SnapToProperty, value); }

    /// <summary>The zone of the days the history is loaded in, the names over the plot and the round times; the device's when null.</summary>
    public TimeZoneInfo? TimeZone { get => (TimeZoneInfo?)GetValue(TimeZoneProperty); set => SetValue(TimeZoneProperty, value); }

    /// <summary>How many local days <see cref="Source"/> is asked for at a time. 1 by default; more for a long span, such as a column a day over months.</summary>
    public int ChunkDays { get => (int)GetValue(ChunkDaysProperty); set => SetValue(ChunkDaysProperty, value); }

    /// <summary>The names over the plot: a day's, a month's or none. Days by default.</summary>
    public ChartSections Sections { get => (ChartSections)GetValue(SectionsProperty); set => SetValue(SectionsProperty, value); }

    /// <summary>A section's name, as a date format in the current culture: <c>dddd</c> (Tuesday) for days and <c>MMMM yyyy</c> for months when null.</summary>
    public string? SectionFormat { get => (string?)GetValue(SectionFormatProperty); set => SetValue(SectionFormatProperty, value); }

    /// <summary>The name where the long one does not fit: <c>ddd</c> for days and <c>MMM</c> for months when null.</summary>
    public string? ShortSectionFormat { get => (string?)GetValue(ShortSectionFormatProperty); set => SetValue(ShortSectionFormatProperty, value); }

    /// <summary>The names' font, or null for the platform's.</summary>
    public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    /// <summary>What one finger dragging over the chart does: picks a value out (the default) or scrolls. Two fingers always scroll.</summary>
    public ChartDrag OneFinger { get => (ChartDrag)GetValue(OneFingerProperty); set => SetValue(OneFingerProperty, value); }

    /// <summary>Whether the window ends at <see cref="Latest"/> and follows it as it moves on; false once scrolled back.</summary>
    public bool IsAtLatest => (bool)GetValue(IsAtLatestProperty);

    /// <summary>Whether a day in view is still being loaded.</summary>
    public bool IsLoading => (bool)GetValue(IsLoadingProperty);

    const string GlideAnimation = "ChartScrollerGlide", PulseAnimation = "ChartScrollerPulse";
    const float SectionHeight = 22, SectionSize = 13, SectionGap = 8, SectionInset = 4;
    const int MostLoads = 3, KeptWindows = 3;
    static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    readonly Grid _area = [];
    readonly GraphicsView _sections = new() { InputTransparent = true };
    readonly GraphicsView _overlay = new() { InputTransparent = true };
    readonly RowDefinition _sectionRow = new(SectionHeight);
    readonly PanGestureRecognizer _twoFingers = new() { TouchPoints = 2 };
    readonly PanGestureRecognizer _oneFinger = new() { TouchPoints = 1 };
    readonly ScrollMotion _motion = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly SortedDictionary<int, Chunk> _chunks = [];
    IDispatcherTimer? _timer;

    DateTimeOffset _to;
    bool _placed, _following = true, _panning, _gliding, _settingTo, _dirty = true;
    DateTimeOffset _panStart;

    /// <summary>Which way the drag went first: sideways moves the chart, up or down is the page's.</summary>
    int _axis;
    const double AxisSlop = 8;
    (int, int, int, int)? _loadedFor;
    float _pulse;

    sealed class Chunk
    {
        public ChartSlice? Slice;
        public CancellationTokenSource? Loading;
        public DateTime? FailedAt;
        public bool Stale;
    }

    sealed class Drawn(Action<ICanvas, RectF> draw) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect) => draw(canvas, dirtyRect);
    }

    public ChartScroller()
    {
        _sections.Drawable = new Drawn(DrawSections);
        _overlay.Drawable = new Drawn(DrawOverlay);
        _area.Add(_overlay);
        var grid = new Grid { RowDefinitions = { _sectionRow, new RowDefinition(GridLength.Star) }, RowSpacing = 0 };
        grid.Add(_sections, 0, 0);
        grid.Add(_area, 0, 1);
        Content = grid;
        _twoFingers.PanUpdated += OnPan;
        _oneFinger.PanUpdated += OnPan;
        Loaded += (_, _) => OnLoaded();
        Unloaded += (_, _) => OnUnloaded();
    }

    TimeZoneInfo Zone => TimeZone ?? TimeZoneInfo.Local;

    IScrollableChart? Scrollable => Chart as IScrollableChart;

    Thickness Margins => Scrollable?.PlotMargin ?? default;

    double PlotWidth => Chart is { Width: > 0 } chart ? chart.Width - Margins.Left - Margins.Right : 0;

    /// <summary>Seconds of time a point across the plot.</summary>
    double SecondsPerPoint => PlotWidth > 0 ? Span.TotalSeconds / PlotWidth : 0;

    DateTimeOffset MaxTo => Latest ?? DateTimeOffset.Now;

    DateTimeOffset? MinTo => Earliest is { } earliest ? Min(earliest + Span, MaxTo) : null;

    static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    DateTimeOffset Clamp(DateTimeOffset to)
    {
        var max = MaxTo;
        return to > max ? max : MinTo is { } min && to < min ? min : to;
    }

    /// <summary>Moves the window to end at <paramref name="to"/>, gliding there unless <paramref name="animate"/> is false.</summary>
    public Task ScrollToAsync(DateTimeOffset to, bool animate = true)
    {
        var target = Clamp(to);
        if (!_placed || !animate || SecondsPerPoint <= 0)
        {
            this.AbortAnimation(GlideAnimation);
            if (_placed)
                Show(target);
            else
                _to = target;
            Settle();
            return Task.CompletedTask;
        }
        return GlideAsync(target);
    }

    /// <summary>Glides back to <see cref="Latest"/>, where the window follows it again.</summary>
    public Task ScrollToLatestAsync() => ScrollToAsync(MaxTo);

    /// <summary>Says the source has new data: every loaded day is asked for again, and shows what it had until the answer comes.</summary>
    public void InvalidateData()
    {
        foreach (var chunk in _chunks.Values)
            chunk.Stale = true;
        _dirty = true;
        Load();
    }

    /// <summary>Says the source has new data from <paramref name="from"/> up to <paramref name="to"/>, such as a new reading today.</summary>
    public void InvalidateData(DateTimeOffset from, DateTimeOffset to)
    {
        var (first, last) = ChartChunks.Range(from, to, Zone, ChunkDays);
        foreach (var (key, chunk) in _chunks)
            if (key >= first && key <= last)
                chunk.Stale = true;
        _dirty = true;
        Load();
    }

    void OnChartChanged(View? old, View? chart)
    {
        if (old is not null)
        {
            old.SizeChanged -= OnChartSized;
            old.HandlerChanged -= OnChartHandlerChanged;
            old.GestureRecognizers.Remove(_twoFingers);
            old.GestureRecognizers.Remove(_oneFinger);
            _area.Remove(old);
        }
        _placed = false;
        if (chart is null)
            return;
        _area.Insert(0, chart);
        chart.SizeChanged += OnChartSized;
        chart.HandlerChanged += OnChartHandlerChanged;
        Recognize();
        Publish();
        Place();
    }

    void OnChartSized(object? sender, EventArgs e)
    {
        if (_placed)
            Show(_panning || _gliding ? _to : Clamp(_following ? MaxTo : _to));
        else
            Place();
    }

    /// <summary>Puts the window where it starts, once the chart has a width to measure time in.</summary>
    void Place()
    {
        if (_placed || Scrollable is null || PlotWidth <= 0)
            return;
        _placed = true;
        _to = Clamp(To ?? MaxTo);
        Show(_to);
        Settle();
    }

    /// <summary>Gestures as <see cref="OneFinger"/> asks: two fingers always scroll; one does too with a mouse, or when set to.</summary>
    void Recognize()
    {
        if (Chart is not { } chart)
            return;
        if (!chart.GestureRecognizers.Contains(_twoFingers))
            chart.GestureRecognizers.Add(_twoFingers);
        var one = OneFinger == ChartDrag.Scroll || DeviceInfo.Idiom == DeviceIdiom.Desktop;
        if (one && !chart.GestureRecognizers.Contains(_oneFinger))
            chart.GestureRecognizers.Add(_oneFinger);
        else if (!one)
            chart.GestureRecognizers.Remove(_oneFinger);
        if (!_panning && !_gliding && Scrollable is { } scrollable)
            scrollable.SelectsByDragging = DragSelects;
        // The platform's recognizers are made from the list after this, so they are tuned a moment later.
        Dispatcher.Dispatch(TunePlatform);
    }

    void OnChartHandlerChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(TunePlatform);

    /// <summary>
    /// On iOS and the Mac a drag that starts more up or down than sideways is never the chart's, so the page around it
    /// still scrolls; on the Mac the trackpad's two-finger swipe and the mouse wheel scroll the chart too.
    /// </summary>
    void TunePlatform()
    {
#if IOS || MACCATALYST
        if (Chart?.Handler is not IViewHandler handler)
            return;
        foreach (var view in new[] { handler.ContainerView as UIKit.UIView, handler.PlatformView as UIKit.UIView })
        {
            foreach (var recognizer in view?.GestureRecognizers ?? [])
            {
                if (recognizer is not UIKit.UIPanGestureRecognizer pan)
                    continue;
                // Two fingers on the chart always mean it: which way they go is decided once they have moved
                // (OnPan). One finger begins only on a drag that has gone more sideways than up or down, measured
                // from where it landed, so a page scroll starting on the chart stays the page's.
                if (pan.MinimumNumberOfTouches <= 1)
                    pan.ShouldBegin = r =>
                    {
                        var moved = ((UIKit.UIPanGestureRecognizer)r).TranslationInView(r.View);
                        return Math.Abs(moved.X) >= Math.Abs(moved.Y);
                    };
                // Beside the chart's own touch (the finger that picks a value out), never beside the page's scrolling.
                pan.ShouldRecognizeSimultaneously = (_, other) => other.View is not UIKit.UIScrollView;
#if MACCATALYST
                pan.AllowedScrollTypesMask = UIKit.UIScrollTypeMask.All;
#endif
            }
        }
#endif
    }

    /// <summary>At rest, a drag picks a value out where one finger is for that, and a mouse always points.</summary>
    bool DragSelects => OneFinger == ChartDrag.Select || DeviceInfo.Idiom == DeviceIdiom.Desktop;

    void OnToChanged(DateTimeOffset? to)
    {
        if (_settingTo || !_placed)
            return;
        this.AbortAnimation(GlideAnimation);
        _gliding = false;
        Show(Clamp(to ?? MaxTo));
        Settle();
    }

    /// <summary>The span, the limits or the size changed: the window keeps its end, or follows the latest.</summary>
    void Replace()
    {
        if (!_placed || _panning || _gliding)
            return;
        Show(Clamp(_following ? MaxTo : _to));
        Settle();
    }

    /// <summary>Another source, zone or chunk size: everything loaded goes.</summary>
    void Reset()
    {
        foreach (var chunk in _chunks.Values)
            chunk.Loading?.Cancel();
        _chunks.Clear();
        _loadedFor = null;
        _dirty = true;
        Publish();
        Load();
        _sections.Invalidate();
    }

    void OnSectionsChanged()
    {
        _sectionRow.Height = Sections == ChartSections.None ? 0 : SectionHeight;
        _sections.Invalidate();
    }

    void OnLoaded()
    {
        if (Application.Current is { } app)
            app.RequestedThemeChanged += OnThemeChanged;
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(30);
        _timer.Tick += OnTick;
        _timer.Start();
        _dirty = true;
        Load();
    }

    void OnUnloaded()
    {
        if (Application.Current is { } app)
            app.RequestedThemeChanged -= OnThemeChanged;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
        this.AbortAnimation(PulseAnimation);
    }

    void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        _sections.Invalidate();
        _overlay.Invalidate();
    }

    /// <summary>Now moves on, and a day that could not be read is asked for again.</summary>
    void OnTick(object? sender, EventArgs e)
    {
        _dirty = true;
        if (_placed && _following && Latest is null && !_panning && !_gliding)
            Show(MaxTo);
        else
            Load();
    }

    /// <summary>The window, now ending at <paramref name="to"/>: the chart, the names, the pulse and what to load.</summary>
    void Show(DateTimeOffset to)
    {
        _to = to;
        Scrollable?.ShowWindow(to - Span, to);
        _sections.Invalidate();
        _overlay.Invalidate();
        Load();
    }

    /// <summary>A scroll has come to rest: <see cref="To"/> and <see cref="From"/> say where, and the latest is followed if it is there.</summary>
    void Settle()
    {
        var spp = SecondsPerPoint;
        _following = (MaxTo - _to).TotalSeconds <= Math.Max(1, spp * 2);
        SetValue(IsAtLatestPropertyKey, _following);
        SetValue(FromPropertyKey, _placed ? _to - Span : null);
        _settingTo = true;
        To = _following && Latest is null ? null : _to;
        _settingTo = false;
        if (Scrollable is { } chart)
            chart.SelectsByDragging = DragSelects;
    }

    void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        if (Scrollable is not { } chart || SecondsPerPoint <= 0)
            return;
        var now = _clock.Elapsed.TotalMilliseconds;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                this.AbortAnimation(GlideAnimation);
                _gliding = false;
                _panning = true;
                _axis = 0;
                _panStart = _to;
                _motion.Reset();
                _motion.Add(now, 0);
                // A second finger: whatever the first began to pick out lets go at once.
                if (ReferenceEquals(sender, _twoFingers))
                {
                    chart.SelectsByDragging = false;
                    chart.ClearSelection();
                }
                break;
            case GestureStatus.Running:
                if (!_panning)
                    return;
                if (_axis == 0)
                {
                    if (Math.Abs(e.TotalY) > AxisSlop && Math.Abs(e.TotalY) > Math.Abs(e.TotalX))
                        _axis = -1;
                    else if (Math.Abs(e.TotalX) > AxisSlop)
                    {
                        _axis = 1;
                        chart.SelectsByDragging = false;
                        chart.ClearSelection();
                    }
                }
                if (_axis != 1)
                    return;
                _motion.Add(now, e.TotalX);
                Show(Banded(_panStart - TimeSpan.FromSeconds(e.TotalX * SecondsPerPoint)));
                break;
            case GestureStatus.Completed or GestureStatus.Canceled:
                if (!_panning)
                    return;
                _panning = false;
                if (_axis != 1)
                {
                    Settle();
                    return;
                }
                // The content follows the finger, so time runs the other way.
                _ = GlideAsync(Rest(-_motion.Velocity(now)));
                break;
        }
    }

    /// <summary>A drag past an end gives less and less, as iOS's rubber band does.</summary>
    DateTimeOffset Banded(DateTimeOffset to)
    {
        var spp = SecondsPerPoint;
        var max = MaxTo;
        if (to > max)
            return max + TimeSpan.FromSeconds(ScrollMotion.RubberBand((to - max).TotalSeconds / spp, PlotWidth) * spp);
        if (MinTo is { } min && to < min)
            return min + TimeSpan.FromSeconds(ScrollMotion.RubberBand((to - min).TotalSeconds / spp, PlotWidth) * spp);
        return to;
    }

    /// <summary>Where a flick at <paramref name="velocity"/> points a millisecond, along time, comes to rest.</summary>
    DateTimeOffset Rest(double velocity)
    {
        var (max, min) = (MaxTo, MinTo);
        if (_to >= max || (min is { } low && _to <= low))
            return Clamp(_to);
        var target = Clamp(_to + TimeSpan.FromSeconds(ScrollMotion.Carry(velocity) * SecondsPerPoint));
        if (SnapTo is { } step && target < max && (min is not { } floor || target > floor))
            target = Clamp(ScrollMotion.Snap(target - Span, step, Zone) + Span);
        return target;
    }

    Task GlideAsync(DateTimeOffset target)
    {
        this.AbortAnimation(GlideAnimation);
        var start = _to;
        var distance = (target - start).TotalSeconds / Math.Max(SecondsPerPoint, 1e-9);
        if (Math.Abs(distance) < 0.5)
        {
            _gliding = false;
            Show(target);
            Settle();
            return Task.CompletedTask;
        }
        var done = new TaskCompletionSource();
        _gliding = true;
        new Animation(t => Show(start + (target - start) * t), 0, 1, Easing.CubicOut)
            .Commit(this, GlideAnimation, rate: 16, length: (uint)ScrollMotion.Duration(distance), finished: (_, cancelled) =>
            {
                if (!cancelled)
                {
                    _gliding = false;
                    Settle();
                }
                done.TrySetResult();
            });
        return done.Task;
    }

    /// <summary>
    /// Asks for the days in the window and a window either way that are not there yet, the window's first, at most
    /// <see cref="MostLoads"/> at a time, and lets go of the days more than <see cref="KeptWindows"/> windows away.
    /// </summary>
    void Load()
    {
        if (Source is not { } source || Scrollable is null || !_placed)
        {
            UpdateLoading();
            return;
        }
        var (zone, days, span) = (Zone, ChunkDays, Span);
        var from = _to - span;
        var max = MaxTo;
        var lowest = Earliest;
        var (viewFirst, viewLast) = ChartChunks.Range(from, _to, zone, days);
        var (needFirst, needLast) = ChartChunks.Range(lowest is { } e ? Max(from - span, e) : from - span, Min(_to + span, max), zone, days);
        var keep = KeptWindows * span;
        var (keepFirst, keepLast) = ChartChunks.Range(from - keep, _to + keep, zone, days);
        var key = (viewFirst, viewLast, needFirst, needLast);
        if (!_dirty && _loadedFor == key)
            return;
        _loadedFor = key;
        _dirty = false;

        var dropped = false;
        foreach (var far in _chunks.Keys.Where(k => k < keepFirst || k > keepLast).ToList())
        {
            _chunks[far].Loading?.Cancel();
            dropped |= _chunks[far].Slice is not null;
            _chunks.Remove(far);
        }
        if (dropped)
            Publish();

        var running = _chunks.Values.Count(c => c.Loading is not null);
        // The window's days newest first, then outwards from it.
        var order = Enumerable.Range(viewFirst, viewLast - viewFirst + 1).Reverse()
            .Concat(Enumerable.Range(needFirst, Math.Max(0, needLast - needFirst + 1)).Where(k => k < viewFirst || k > viewLast)
                .OrderBy(k => k < viewFirst ? viewFirst - k : k - viewLast));
        foreach (var chunkKey in order)
        {
            if (running >= MostLoads)
            {
                _dirty = true;
                break;
            }
            if (chunkKey < needFirst || chunkKey > needLast)
                continue;
            _chunks.TryGetValue(chunkKey, out var chunk);
            if (chunk is { Loading: not null } || chunk is { Slice: not null, Stale: false })
                continue;
            if (chunk?.FailedAt is { } failed && DateTime.UtcNow - failed < RetryAfter)
                continue;
            if (chunk is null)
                _chunks[chunkKey] = chunk = new Chunk();
            running++;
            Fetch(source, chunkKey, chunk);
        }
        UpdateLoading();
    }

    async void Fetch(IChartHistory source, int key, Chunk chunk)
    {
        var loading = new CancellationTokenSource();
        chunk.Loading = loading;
        var (from, to) = ChartChunks.Bounds(key, Zone, ChunkDays);
        try
        {
            var slice = await source.LoadAsync(from, to, loading.Token);
            if (loading.IsCancellationRequested)
                return;
            chunk.Slice = slice;
            chunk.Stale = false;
            chunk.FailedAt = null;
            Publish();
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            chunk.FailedAt = DateTime.UtcNow;
            Debug.WriteLine($"[Spine] ChartScroller: the history from {from:O} to {to:O} could not be read: {exception.Message}");
        }
        finally
        {
            if (chunk.Loading == loading)
                chunk.Loading = null;
            loading.Dispose();
            _dirty = true;
            Load();
            _overlay.Invalidate();
        }
    }

    /// <summary>What is loaded, merged oldest first, to the chart.</summary>
    void Publish() => Scrollable?.ShowHistory(ChartSlice.Merge(_chunks.Values.Select(c => c.Slice).OfType<ChartSlice>()));

    /// <summary>Whether a day in view has nothing to show yet, and the pulse over it.</summary>
    void UpdateLoading()
    {
        var loading = _placed && VisibleLoading().Any();
        if (loading == IsLoading)
            return;
        SetValue(IsLoadingPropertyKey, loading);
        this.AbortAnimation(PulseAnimation);
        _pulse = 0;
        if (loading && !ReducedMotion.IsOn)
            new Animation(v => { _pulse = (float)v; _overlay.Invalidate(); }, 0, 1)
                .Commit(this, PulseAnimation, rate: 33, length: 1400, easing: Easing.SinInOut, repeat: () => IsLoading);
        _overlay.Invalidate();
    }

    /// <summary>The days in view still waiting for their first answer.</summary>
    IEnumerable<(DateTimeOffset From, DateTimeOffset To)> VisibleLoading()
    {
        var (first, last) = ChartChunks.Range(_to - Span, _to, Zone, ChunkDays);
        foreach (var (key, chunk) in _chunks)
            if (key >= first && key <= last && chunk.Slice is null && chunk.Loading is not null)
                yield return ChartChunks.Bounds(key, Zone, ChunkDays);
    }

    float XOf(DateTimeOffset time) => (float)(Margins.Left + (time - (_to - Span)).TotalSeconds / SecondsPerPoint);

    void DrawOverlay(ICanvas canvas, RectF rect)
    {
        if (!_placed || SecondsPerPoint <= 0)
            return;
        var dark = Application.Current?.RequestedTheme != AppTheme.Light;
        var (left, right) = ((float)Margins.Left, (float)(Margins.Left + PlotWidth));
        canvas.FillColor = (dark ? Colors.White : Colors.Black).WithAlpha(0.035f + 0.05f * _pulse);
        foreach (var (from, to) in VisibleLoading())
        {
            var x0 = Math.Max(left, XOf(from));
            var x1 = Math.Min(right, XOf(to));
            if (x1 - x0 > 1)
                canvas.FillRoundedRectangle(x0 + 1, rect.Y + 4, x1 - x0 - 2, rect.Height - 8, 6);
        }
    }

    void DrawSections(ICanvas canvas, RectF rect)
    {
        if (Sections == ChartSections.None || !_placed || SecondsPerPoint <= 0)
            return;
        var zone = Zone;
        var from = _to - Span;
        var culture = CultureInfo.CurrentCulture;
        var font = new Microsoft.Maui.Graphics.Font(FontFamily, FontWeights.SemiBold);
        var (longFormat, shortFormat) = Sections == ChartSections.Days
            ? (SectionFormat ?? "dddd", ShortSectionFormat ?? "ddd")
            : (SectionFormat ?? "MMMM yyyy", ShortSectionFormat ?? "MMM");

        var day = ChartChunks.Day(from, zone);
        if (Sections == ChartSections.Months)
        {
            var date = DateOnly.FromDayNumber(day);
            day = new DateOnly(date.Year, date.Month, 1).DayNumber;
        }
        var names = new List<(string Long, string Short)>();
        var sections = new List<ChartSection>();
        var left = (float)Margins.Left;
        while (true)
        {
            var start = ChartChunks.StartOf(day, zone);
            if (start >= _to)
                break;
            var date = DateOnly.FromDayNumber(day).ToDateTime(TimeOnly.MinValue);
            var (name, brief) = (Capital(date.ToString(longFormat, culture), culture), Capital(date.ToString(shortFormat, culture), culture));
            names.Add((name, brief));
            sections.Add(new ChartSection(XOf(start) - left, Measure(canvas, name, font), Measure(canvas, brief, font)));
            day = Sections == ChartSections.Days ? day + 1 : DateOnly.FromDayNumber(day).AddMonths(1).DayNumber;
        }

        var dark = Application.Current?.RequestedTheme != AppTheme.Light;
        var ink = dark ? Colors.White : Color.FromArgb("#1D1D1F");
        canvas.Font = font;
        canvas.FontSize = SectionSize;
        foreach (var placed in SectionLayout.Place(sections, (float)PlotWidth, SectionGap, SectionInset))
        {
            var text = placed.Short ? names[placed.Index].Short : names[placed.Index].Long;
            canvas.FontColor = ink.WithAlpha(placed.Opacity);
            // A box wider than the name, so a measure a little too narrow never cuts it short.
            canvas.DrawString(text, left + placed.X, rect.Y, (placed.Short ? sections[placed.Index].ShortWidth : sections[placed.Index].LongWidth) + 40,
                rect.Height, HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    /// <summary>A name's width, with a little to spare: the platform's measure can come out narrower than what it draws.</summary>
    static float Measure(ICanvas canvas, string text, Microsoft.Maui.Graphics.Font font) =>
        canvas.GetStringSize(text, font, SectionSize).Width * 1.06f + 2;

    static string Capital(string text, CultureInfo culture) => text.Length == 0 ? text : char.ToUpper(text[0], culture) + text[1..];
}
