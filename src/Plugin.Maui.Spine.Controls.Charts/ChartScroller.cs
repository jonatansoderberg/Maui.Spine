using System.Diagnostics;
using System.Globalization;
using Plugin.Maui.Spine.Common;
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

    public static readonly BindableProperty OneFingerProperty = BindableProperty.Create(nameof(OneFinger), typeof(ChartDrag), typeof(ChartScroller), ChartDrag.Scroll,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).Recognize());

    static readonly BindablePropertyKey FromPropertyKey = BindableProperty.CreateReadOnly(nameof(From), typeof(DateTimeOffset?), typeof(ChartScroller), null);
    public static readonly BindableProperty FromProperty = FromPropertyKey.BindableProperty;

    public static readonly BindableProperty NowAtProperty = BindableProperty.Create(nameof(NowAt), typeof(double), typeof(ChartScroller), 1.0,
        validateValue: static (_, value) => (double)value is >= 0 and <= 1,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).Replace());

    public static readonly BindableProperty ShowNowButtonProperty = BindableProperty.Create(nameof(ShowNowButton), typeof(bool), typeof(ChartScroller), true,
        propertyChanged: static (b, _, _) => ((ChartScroller)b).OnSectionsChanged());

    static readonly BindablePropertyKey IsNowLaterPropertyKey = BindableProperty.CreateReadOnly(nameof(IsNowLater), typeof(bool), typeof(ChartScroller), false);
    public static readonly BindableProperty IsNowLaterProperty = IsNowLaterPropertyKey.BindableProperty;

    static readonly BindablePropertyKey IsAtNowPropertyKey = BindableProperty.CreateReadOnly(nameof(IsAtNow), typeof(bool), typeof(ChartScroller), true);
    public static readonly BindableProperty IsAtNowProperty = IsAtNowPropertyKey.BindableProperty;

    static readonly BindablePropertyKey IsLoadingPropertyKey = BindableProperty.CreateReadOnly(nameof(IsLoading), typeof(bool), typeof(ChartScroller), false);
    public static readonly BindableProperty IsLoadingProperty = IsLoadingPropertyKey.BindableProperty;

    /// <summary>The chart moved along time: a <see cref="LineChart"/>, a <see cref="ColumnChart"/> or another <see cref="IScrollableChart"/>.</summary>
    public View? Chart { get => (View?)GetValue(ChartProperty); set => SetValue(ChartProperty, value); }

    /// <summary>Where the history comes from, a stretch of whole local days at a time. Without one the chart shows what it was given.</summary>
    public IChartHistory? Source { get => (IChartHistory?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>How much time the plot shows at once. A day by default.</summary>
    public TimeSpan Span { get => (TimeSpan)GetValue(SpanProperty); set => SetValue(SpanProperty, value); }

    /// <summary>
    /// The end of the time in view; where now puts it (<see cref="NowAt"/>) when null, following now. Set when a scroll comes to rest, and setting it moves
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

    /// <summary>
    /// What one finger dragging over the chart does: scrolls (the default), with a tap or a held press picking a value
    /// out, or picks a value out at once. Two fingers always scroll.
    /// </summary>
    public ChartDrag OneFinger { get => (ChartDrag)GetValue(OneFingerProperty); set => SetValue(OneFingerProperty, value); }

    /// <summary>
    /// Where now stands across the window when the chart opens and when it goes back to now: 1 (the default) at the
    /// trailing edge, for a history that ends now; 0 at the leading edge, for a forecast; between, for a history and a
    /// forecast in one chart. With <see cref="SnapTo"/> the window rests on round times with now in view. Under 1, a
    /// line marks now wherever it is in view.
    /// </summary>
    public double NowAt { get => (double)GetValue(NowAtProperty); set => SetValue(NowAtProperty, value); }

    /// <summary>
    /// Whether the window is where now puts it (<see cref="NowAt"/>) and moves on with it; false once scrolled away. A
    /// page shows its "Now" button while it is false, and <see cref="ScrollToNowAsync"/> goes back.
    /// </summary>
    public bool IsAtNow => (bool)GetValue(IsAtNowProperty);

    /// <summary>
    /// Whether now lies later than the window (scrolled back into the history), so the way back to it points forward;
    /// false when it lies earlier (scrolled ahead into a forecast) or the window is at now.
    /// </summary>
    public bool IsNowLater => (bool)GetValue(IsNowLaterProperty);

    /// <summary>
    /// Whether a small "Now" pill shows in the row of names, on the side where now lies, while the window is away from
    /// now; a tap goes back. Its text is Spine's <c>Spine.Charts.Now</c>. True by default; off for a page with a button of
    /// its own (<see cref="IsAtNow"/>, <see cref="IsNowLater"/>).
    /// </summary>
    public bool ShowNowButton { get => (bool)GetValue(ShowNowButtonProperty); set => SetValue(ShowNowButtonProperty, value); }

    /// <summary>Whether a day in view is still being loaded.</summary>
    public bool IsLoading => (bool)GetValue(IsLoadingProperty);

    const string GlideAnimation = "ChartScrollerGlide", PulseAnimation = "ChartScrollerPulse";
    const float SectionHeight = 22, SectionSize = 13, SectionGap = 8, SectionInset = 4;
    const int MostLoads = 3, KeptWindows = 3;
    static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    readonly Grid _area = [];
    readonly GraphicsView _sections = new() { InputTransparent = true };
    readonly GraphicsView _overlay = new() { InputTransparent = true };
    readonly Label _nowText = new() { FontSize = 13, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
    readonly Border _nowPill = new()
    {
        StrokeThickness = 0,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 11 },
        Padding = new Thickness(9, 0),
        HeightRequest = 22,
        VerticalOptions = LayoutOptions.Center,
        IsVisible = false,
    };
    readonly RowDefinition _sectionRow = new(SectionHeight);
    readonly PanGestureRecognizer _twoFingers = new() { TouchPoints = 2 };
    readonly PanGestureRecognizer _oneFinger = new() { TouchPoints = 1 };
    readonly PointerGestureRecognizer _touch = new();

    /// <summary>How long a finger rests before its press picks a value out, and how far it may wander meanwhile.</summary>
    const int HoldMilliseconds = 300;
    const double HoldSlop = 10;

    int _press;
    Point? _pressedAt;
    bool _scrubbing, _panScrubs;
    double _scrubFrom;
    int? _restDay;
    readonly ScrollMotion _motion = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly SortedDictionary<int, Chunk> _chunks = [];
    IDispatcherTimer? _timer;

    DateTimeOffset _to;
    bool _placed, _following = true, _panning, _gliding, _settingTo, _dirty = true;
    DateTimeOffset _panStart;

    /// <summary>Where a glide is going: its days are loaded first, not those it passes on the way.</summary>
    DateTimeOffset? _glideTarget;

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

    static ChartScroller() => SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(ChartScroller).Assembly));

    public ChartScroller()
    {
        _sections.Drawable = new Drawn(DrawSections);
        _overlay.Drawable = new Drawn(DrawOverlay);
        _area.Add(_overlay);
        var grid = new Grid { RowDefinitions = { _sectionRow, new RowDefinition(GridLength.Star) }, RowSpacing = 0 };
        grid.Add(_sections, 0, 0);
        _nowPill.Content = _nowText;
        var goBack = new TapGestureRecognizer();
        goBack.Tapped += (_, _) => _ = ScrollToNowAsync();
        _nowPill.GestureRecognizers.Add(goBack);
        _nowPill.SizeChanged += (_, _) => _sections.Invalidate();
        SemanticProperties.SetHint(_nowPill, SpineStrings.Current["Spine.Charts.Now"]);
        grid.Add(_nowPill, 0, 0);
        grid.Add(_area, 0, 1);
        Content = grid;
        _twoFingers.PanUpdated += OnPan;
        _oneFinger.PanUpdated += OnPan;
        _touch.PointerPressed += OnPressed;
        _touch.PointerMoved += OnTouchMoved;
        _touch.PointerReleased += (_, _) => Lift();
        _touch.PointerExited += (_, _) => Lift();
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

    /// <summary>Glides back to now, put where <see cref="NowAt"/> says, where the window follows it again.</summary>
    public Task ScrollToNowAsync() => ScrollToAsync(Home);

    /// <summary>
    /// Where now puts the window's end: now at <see cref="NowAt"/> across it, on round times with now still in view, and
    /// within the history.
    /// </summary>
    DateTimeOffset Home
    {
        get
        {
            var now = DateTimeOffset.Now;
            var to = now + Span * (1 - NowAt);
            if (SnapTo is { } step)
                to = NowAt >= 0.5 ? ScrollMotion.SnapUp(to, step, Zone) : ScrollMotion.SnapDown(to - Span, step, Zone) + Span;
            return Clamp(to);
        }
    }

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
            old.GestureRecognizers.Remove(_touch);
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
            Show(_panning || _gliding ? _to : _following ? Home : Clamp(_to));
        else
            Place();
    }

    /// <summary>Puts the window where it starts, once the chart has a width to measure time in.</summary>
    void Place()
    {
        if (_placed || Scrollable is null || PlotWidth <= 0)
            return;
        _placed = true;
        _to = To is { } to ? Clamp(to) : Home;
        Show(_to);
        Settle();
        _restDay = ChartChunks.Day(_to - Span / 2, Zone);
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
        if (!chart.GestureRecognizers.Contains(_touch))
            chart.GestureRecognizers.Add(_touch);
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
                // A held press (a scrub) keeps the finger whichever way it goes, so the page does not move under it.
                if (pan.MinimumNumberOfTouches <= 1)
                {
                    pan.ShouldBegin = r =>
                    {
                        var moved = ((UIKit.UIPanGestureRecognizer)r).TranslationInView(r.View);
                        return _scrubbing || Math.Abs(moved.X) >= Math.Abs(moved.Y);
                    };
                    // Everything around the chart that pans waits for it to say whether a drag is its own: the
                    // page's scrolling, and the back swipe iOS 26 takes from anywhere on a page, which a sideways
                    // drag on the chart would otherwise start.
                    for (var parent = view?.Superview; parent is not null; parent = parent.Superview)
                        foreach (var other in parent.GestureRecognizers ?? [])
                            if (other is UIKit.UIPanGestureRecognizer outer && !ReferenceEquals(outer, pan))
                                outer.RequireGestureRecognizerToFail(pan);
                }
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
        Show(to is { } at ? Clamp(at) : Home);
        Settle();
    }

    /// <summary>The span, the limits or the size changed: the window keeps its end, or follows the latest.</summary>
    void Replace()
    {
        Publish();
        if (!_placed || _panning || _gliding)
            return;
        Show(_following ? Home : Clamp(_to));
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
        _sectionRow.Height = Sections == ChartSections.None && !ShowNowButton ? 0 : SectionHeight;
        UpdateNowPill();
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

    /// <summary>The "Now" pill: shown away from now, on the side where now lies, lined up with the plot's edge.</summary>
    void UpdateNowPill()
    {
        var show = ShowNowButton && _placed && !_following;
        _nowPill.IsVisible = show;
        if (!show)
        {
            _sections.Invalidate();
            return;
        }
        var later = IsNowLater;
        var now = SpineStrings.Current["Spine.Charts.Now"];
        _nowText.Text = later ? $"{now} ›" : $"‹ {now}";
        _nowPill.HorizontalOptions = later ? LayoutOptions.End : LayoutOptions.Start;
        _nowPill.Margin = later ? new Thickness(0, 0, Margins.Right, 0) : new Thickness(Margins.Left, 0, 0, 0);
        var dark = Application.Current?.RequestedTheme != AppTheme.Light;
        _nowPill.BackgroundColor = (dark ? Colors.White : Colors.Black).WithAlpha(dark ? 0.16f : 0.07f);
        _nowText.TextColor = dark ? Colors.White : Color.FromArgb("#1D1D1F");
        _sections.Invalidate();
    }

    void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        UpdateNowPill();
        _sections.Invalidate();
        _overlay.Invalidate();
    }

    /// <summary>Now moves on, and a day that could not be read is asked for again.</summary>
    void OnTick(object? sender, EventArgs e)
    {
        _dirty = true;
        if (_placed && _following && !_panning && !_gliding)
            Show(Home);
        else
            Load();
        _overlay.Invalidate();
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
        var home = Home;
        _following = Math.Abs((home - _to).TotalSeconds) <= Math.Max(1, spp * 2);
        SetValue(IsAtNowPropertyKey, _following);
        SetValue(IsNowLaterPropertyKey, !_following && home > _to);
        UpdateNowPill();
        SetValue(FromPropertyKey, _placed ? _to - Span : null);
        _settingTo = true;
        To = _following ? null : _to;
        _settingTo = false;
        if (Scrollable is { } chart)
            chart.SelectsByDragging = DragSelects;
    }

    /// <summary>
    /// A finger lands: it stops a glide, as a finger stops a scroll view; resting where it landed it becomes a scrub after
    /// <see cref="HoldMilliseconds"/>, with a tick, where one finger scrolls. A mouse points instead.
    /// </summary>
    void OnPressed(object? sender, PointerEventArgs e)
    {
        if (DeviceInfo.Idiom == DeviceIdiom.Desktop || Chart is not { } chart)
            return;
        var press = ++_press;
        _pressedAt = e.GetPosition(chart);
        if (_gliding)
        {
            this.AbortAnimation(GlideAnimation);
            _gliding = false;
            Settle();
            _pressedAt = null;
            return;
        }
        if (OneFinger != ChartDrag.Scroll)
            return;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(HoldMilliseconds), () =>
        {
            if (press == _press && _pressedAt is { } at && !_panning)
                StartScrub(at.X);
        });
    }

    void OnTouchMoved(object? sender, PointerEventArgs e)
    {
        if (Chart is not { } chart || e.GetPosition(chart) is not { } at)
            return;
        if (_scrubbing)
            Scrollable?.HoldSelection(at.X);
        else if (_pressedAt is { } from && Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y) > HoldSlop)
            _pressedAt = null;
    }

    void StartScrub(double x)
    {
        _scrubbing = true;
        _scrubFrom = x;
        HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        Scrollable?.HoldSelection(x);
    }

    /// <summary>The finger lifts: a scrub ends, and its value goes as a tapped one does.</summary>
    void Lift()
    {
        _press++;
        _pressedAt = null;
        if (!_scrubbing)
            return;
        _scrubbing = false;
        Scrollable?.HoldSelection(null);
    }

    void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        // A held press that moves is a scrub, not a scroll: the value follows the finger.
        if (!ReferenceEquals(sender, _twoFingers) && (_panScrubs || (_scrubbing && e.StatusType == GestureStatus.Started)))
        {
            _panScrubs = e.StatusType is not (GestureStatus.Completed or GestureStatus.Canceled);
            if (e.StatusType == GestureStatus.Running)
                Scrollable?.HoldSelection(_scrubFrom + e.TotalX);
            else if (!_panScrubs)
                Lift();
            return;
        }
        // Any scroll ends the wait for a held press.
        if (e.StatusType == GestureStatus.Started)
        {
            _press++;
            _pressedAt = null;
            if (_scrubbing)
            {
                _scrubbing = false;
                Scrollable?.ClearSelection();
            }
        }
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

    /// <summary>A light tick when a scroll comes to rest on another day than the last one, so the day can be felt.</summary>
    void Tick()
    {
        var day = ChartChunks.Day(_to - Span / 2, Zone);
        if (_restDay is { } before && before != day)
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        _restDay = day;
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
        _glideTarget = target;
        _dirty = true;
        new Animation(t => Show(start + (target - start) * t), 0, 1, Easing.CubicOut)
            .Commit(this, GlideAnimation, rate: 16, length: (uint)ScrollMotion.Duration(distance), finished: (_, cancelled) =>
            {
                _glideTarget = null;
                if (!cancelled)
                {
                    _gliding = false;
                    Settle();
                    Tick();
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
        // Gliding, the days where it will rest are the ones to have; those it passes are gone in a moment.
        var at = _glideTarget ?? _to;
        var from = at - span;
        var max = MaxTo;
        var lowest = Earliest;
        var (viewFirst, viewLast) = ChartChunks.Range(from, at, zone, days);
        var (needFirst, needLast) = ChartChunks.Range(lowest is { } e ? Max(from - span, e) : from - span, Min(at + span, max), zone, days);
        var keep = KeptWindows * span;
        var (keepFirst, keepLast) = ChartChunks.Range(Min(from, _to - span) - keep, Max(at, _to) + keep, zone, days);
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
    /// <remarks>
    /// Only what lies between <see cref="Earliest"/> and <see cref="Latest"/>: a source that answers whole weeks may give
    /// days past an end, and drawn there they would look reachable while a drag past the end springs back.
    /// </remarks>
    void Publish() => Scrollable?.ShowHistory(ChartSlice.Merge(_chunks.Values.Select(c => c.Slice).OfType<ChartSlice>()).Within(Earliest, Latest));

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
        if (Source is null)
            yield break;
        var from = Earliest is { } earliest ? Max(_to - Span, earliest) : _to - Span;
        var to = Min(_to, MaxTo);
        if (to <= from)
            yield break;
        // Asked for or still waiting its turn: either way it has nothing to show yet. One that failed waits quietly.
        var (first, last) = ChartChunks.Range(from, to, Zone, ChunkDays);
        for (var key = first; key <= last; key++)
            if (!_chunks.TryGetValue(key, out var chunk) || (chunk.Slice is null && chunk.FailedAt is null))
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

        // Now, on a chart made to reach past it (NowAt under 1): a line through the plot, so what has been and what is
        // forecast part. A history that ends now has nothing past it to part from.
        var now = XOf(DateTimeOffset.Now);
        if (NowAt < 1 && now > left + 2 && now < right - 2)
        {
            canvas.StrokeColor = (dark ? Colors.White : Colors.Black).WithAlpha(0.45f);
            canvas.StrokeSize = 1.5f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine(now, rect.Y + 2, now, rect.Bottom - 2);
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
        // The names keep clear of the "Now" pill, on whichever side it stands.
        var reserve = _nowPill.IsVisible ? (float)Math.Max(_nowPill.Width, 56) + SectionGap : 0;
        var (leading, trailing) = IsNowLater ? (0f, reserve) : (reserve, 0f);
        for (var i = 0; i < sections.Count; i++)
            sections[i] = sections[i] with { Offset = sections[i].Offset - leading };
        left += leading;
        var room = (float)PlotWidth - leading - trailing;
        // A name being pushed out slides under nothing: it is cut off where the row's room ends.
        canvas.ClipRectangle(left, rect.Y, room, rect.Height);
        foreach (var placed in SectionLayout.Place(sections, room, SectionGap, SectionInset))
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
