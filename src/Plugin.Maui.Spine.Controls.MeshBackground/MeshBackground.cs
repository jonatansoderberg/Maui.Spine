using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Maui.Layouts;
using Plugin.Maui.Spine.Core;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A mesh gradient: a grid of coloured points blended smoothly into each other, that can drift
/// slowly. Put it behind other content in a <see cref="Grid"/>; it is a background for glass and
/// blur surfaces (<c>Material.Kind</c>), heroes, onboarding and empty states.
/// </summary>
/// <remarks>
/// Unset <see cref="Colors"/> come from <see cref="Preset"/>, light or dark with the theme; the
/// <see cref="MeshPreset.Accent"/> preset follows the app's accent. While it drifts it redraws at most
/// <see cref="FrameRate"/> times a second, and not at all while it is detached, hidden (itself or an
/// ancestor), scrolled out of sight, or the app is in the background. With Reduce Motion on it stays still. It draws a smooth
/// gradient at a fraction of the screen's resolution and lets the platform scale it up, so a full-screen
/// mesh costs the CPU a small bitmap per frame.
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <Grid>
///     <MeshBackground Preset="Aurora" Drift="Slow" />
///     <Border Material.Kind="Glass" StrokeShape="RoundRectangle 24" Padding="20" VerticalOptions="Center">
///         <Label Text="Over the aurora" />
///     </Border>
/// </Grid>
/// ]]></code>
/// </example>
public sealed class MeshBackground : ContentView
{
    /// <summary>Points of the drawn bitmap per pixel: the gradient is drawn at half the resolution of the view in points.</summary>
    private const double PointsPerPixel = 2;

    /// <summary>The time a point of speed 1 takes to come round at <see cref="MeshDrift.Slow"/>.</summary>
    private const double SlowPeriodSeconds = 30;

    public static readonly BindableProperty ColumnsProperty = BindableProperty.Create(
        nameof(Columns), typeof(int), typeof(MeshBackground), 3,
        coerceValue: static (_, value) => Math.Clamp((int)value, 2, 8),
        propertyChanged: static (b, _, _) => ((MeshBackground)b).OnGridChanged());

    public static readonly BindableProperty RowsProperty = BindableProperty.Create(
        nameof(Rows), typeof(int), typeof(MeshBackground), 3,
        coerceValue: static (_, value) => Math.Clamp((int)value, 2, 8),
        propertyChanged: static (b, _, _) => ((MeshBackground)b).OnGridChanged());

    public static readonly BindableProperty ColorsProperty = BindableProperty.Create(
        nameof(Colors), typeof(IReadOnlyList<Color>), typeof(MeshBackground),
        propertyChanged: static (b, _, _) => ((MeshBackground)b).OnColorsChanged());

    public static readonly BindableProperty PresetProperty = BindableProperty.Create(
        nameof(Preset), typeof(MeshPreset), typeof(MeshBackground), MeshPreset.Accent,
        propertyChanged: static (b, _, _) => ((MeshBackground)b).OnColorsChanged());

    public static readonly BindableProperty DriftProperty = BindableProperty.Create(
        nameof(Drift), typeof(MeshDrift), typeof(MeshBackground), MeshDrift.None,
        propertyChanged: static (b, _, _) => ((MeshBackground)b).OnDriftChanged());

    public static readonly BindableProperty FrameRateProperty = BindableProperty.Create(
        nameof(FrameRate), typeof(int), typeof(MeshBackground), 30,
        coerceValue: static (_, value) => Math.Clamp((int)value, 1, 60),
        propertyChanged: static (b, _, _) => ((MeshBackground)b).OnFrameRateChanged());

    private readonly SKCanvasView _canvas;
    private readonly MeshSurface _surface = new();
    private readonly SKPaint _paint = new() { IsDither = true };
    private readonly Stopwatch _clock = new();

    private IDispatcherTimer? _timer;
    private Window? _window;
    private bool _windowStopped;
    private bool _colorsDirty = true;
    private bool _reduceMotion;
    private double _phase;
    private double _lastTick;
#if ANDROID
    private Android.Graphics.Rect? _visibleRect;
#endif

    public MeshBackground()
    {
        _canvas = new SKCanvasView
        {
            AnchorX = 0,
            AnchorY = 0,
            InputTransparent = true,
            EnableTouchEvents = false,
        };
        _canvas.PaintSurface += OnPaintSurface;
#if ANDROID
        // Android scales a view's drawing up pixel by pixel, which shows the small bitmap's pixels as
        // blocks; a hardware layer composited with a filtering paint scales it up smoothly, as iOS does.
        _canvas.HandlerChanged += (_, _) =>
            (_canvas.Handler?.PlatformView as Android.Views.View)?.SetLayerType(Android.Views.LayerType.Hardware, new Android.Graphics.Paint { FilterBitmap = true });
#endif
        Content = new ScaledCanvasHost(_canvas);

        InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(this, false);

        _surface.Resize(Columns, Rows);

        SpineTheme.Track(this, OnThemeChanged);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        HandlerChanged += (_, _) =>
        {
            // Losing the handler is the only teardown an abandoned Android window gives.
            if (Handler is null)
                Detach();
        };
    }

    /// <summary>Control points across, 2–8. Default 3.</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>Control points down, 2–8. Default 3.</summary>
    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>
    /// One colour per control point, row by row from the top left; a shorter list repeats. In XAML a
    /// comma-separated list: <c>Colors="#FF5E3A, #FFCC00, #34C759"</c>. Unset, the colours come from
    /// <see cref="Preset"/> and follow the theme.
    /// </summary>
    [TypeConverter(typeof(MeshColorsTypeConverter))]
    public IReadOnlyList<Color>? Colors
    {
        get => (IReadOnlyList<Color>?)GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    /// <summary>The colours used while <see cref="Colors"/> is unset. Default <see cref="MeshPreset.Accent"/>.</summary>
    public MeshPreset Preset
    {
        get => (MeshPreset)GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    /// <summary>How fast the points wander. Default <see cref="MeshDrift.None"/>: a still mesh that costs nothing after it is drawn.</summary>
    public MeshDrift Drift
    {
        get => (MeshDrift)GetValue(DriftProperty);
        set => SetValue(DriftProperty, value);
    }

    /// <summary>The most redraws a second while drifting, 1–60. Default 30, which a slow drift does not need more than.</summary>
    public int FrameRate
    {
        get => (int)GetValue(FrameRateProperty);
        set => SetValue(FrameRateProperty, value);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateAnimation();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IsVisibleProperty.PropertyName)
            UpdateAnimation();
    }

    private void OnGridChanged()
    {
        _surface.Resize(Columns, Rows);
        _colorsDirty = true;
        _canvas.InvalidateSurface();
    }

    private void OnColorsChanged()
    {
        _colorsDirty = true;
        _canvas.InvalidateSurface();
    }

    private void OnThemeChanged()
    {
        if (Colors is not { Count: > 0 })
            OnColorsChanged();
    }

    private void OnDriftChanged()
    {
        if (Drift == MeshDrift.None)
            _phase = 0;

        UpdateAnimation();
        _canvas.InvalidateSurface();
    }

    private void OnFrameRateChanged()
    {
        if (_timer is not null)
            _timer.Interval = TimeSpan.FromSeconds(1.0 / FrameRate);
    }

    private void Attach()
    {
        if (_window is null && Window is { } window)
        {
            _window = window;
            window.Stopped += OnWindowStopped;
            window.Resumed += OnWindowResumed;
            window.Activated += OnWindowActivated;
        }

        UpdateAnimation();
    }

    private void Detach()
    {
        if (_window is { } window)
        {
            window.Stopped -= OnWindowStopped;
            window.Resumed -= OnWindowResumed;
            window.Activated -= OnWindowActivated;
            _window = null;
        }

        _windowStopped = false;
        StopTimer();
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        _windowStopped = true;
        UpdateAnimation();
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        _windowStopped = false;
        UpdateAnimation();
    }

    // Reduce Motion is changed in the system's settings, so it is read again whenever the app comes back.
    private void OnWindowActivated(object? sender, EventArgs e) => UpdateAnimation();

    private void UpdateAnimation()
    {
        var reduceMotion = ReduceMotion.IsEnabled;
        if (reduceMotion != _reduceMotion)
        {
            _reduceMotion = reduceMotion;
            _phase = 0;
            _canvas.InvalidateSurface();
        }

        var run = Drift != MeshDrift.None
            && _window is not null
            && !_windowStopped
            && IsVisible
            && Width > 0 && Height > 0
            && !reduceMotion;

        if (run)
            StartTimer();
        else
            StopTimer();
    }

    private void StartTimer()
    {
        if (_timer is not null || Dispatcher is null)
            return;

        _clock.Restart();
        _lastTick = 0;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.0 / FrameRate);
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null)
            return;

        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
        _clock.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // The clock rather than a count of ticks, so a late frame does not slow the drift; capped, so a
        // stall (a breakpoint, a long layout) does not make the mesh jump. The phase is accumulated, so a
        // change of speed carries on from where the points are.
        var now = _clock.Elapsed.TotalSeconds;
        _phase += Math.Min(now - _lastTick, 0.25) * Speed(Drift) * Math.Tau / SlowPeriodSeconds;
        _lastTick = now;

        if (IsShown())
            _canvas.InvalidateSurface();
    }

    /// <summary>
    /// Whether any of the mesh is on screen. A hidden ancestor (an unselected tab, a collapsed panel)
    /// or a scroll view that has moved it out of sight hides it without telling it.
    /// </summary>
    private bool IsShown()
    {
        for (Element? element = this; element is not null; element = element.Parent)
        {
            if (element is VisualElement { IsVisible: false })
                return false;
        }

#if IOS || MACCATALYST
        if (Handler?.PlatformView is UIKit.UIView view && view.Window is { } window)
            return view.ConvertRectToView(view.Bounds, null).IntersectsWith(window.Bounds);
#elif ANDROID
        if (Handler?.PlatformView is Android.Views.View view)
            return view.GetGlobalVisibleRect(_visibleRect ??= new Android.Graphics.Rect());
#endif
        return true;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (e.Info.Width <= 0 || e.Info.Height <= 0)
            return;

        if (_colorsDirty)
        {
            _surface.SetColors(PointColors());
            _colorsDirty = false;
        }

        _surface.Layout(e.Info.Width, e.Info.Height, (float)_phase);

        canvas.DrawVertices(SKVertexMode.Triangles, _surface.Vertices, null, _surface.VertexColors, SKBlendMode.Dst, _surface.Indices, _paint);
    }

    private SKColor[] PointColors()
    {
        if (Colors is { Count: > 0 } colors)
            return MeshPalettes.Repeat([.. colors.Select(c => c.ToSKColor())], Columns, Rows);

        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var accent = SpineTheme.GetAccent(dark ? AppTheme.Dark : AppTheme.Light)?.ToSKColor()
            ?? (dark ? MeshPalettes.DefaultAccentDark : MeshPalettes.DefaultAccentLight);

        return MeshPalettes.Sample(MeshPalettes.Design(Preset, dark, accent), Columns, Rows);
    }

    private static double Speed(MeshDrift drift) => drift switch
    {
        MeshDrift.Medium => 2,
        MeshDrift.Fast => 4,
        _ => 1,
    };

    /// <summary>
    /// Lays the canvas out at a fraction of the host's size and scales it up with a transform, which
    /// the platform's compositor applies: the canvas fills a small bitmap, the screen shows it full size.
    /// The canvas is one point larger than the exact fraction, and the host clips what reaches past its edges.
    /// </summary>
    private sealed class ScaledCanvasHost : Layout, ILayoutManager
    {
        private readonly SKCanvasView _canvas;

        public ScaledCanvasHost(SKCanvasView canvas)
        {
            _canvas = canvas;
            IsClippedToBounds = true;
            Children.Add(canvas);
        }

        protected override ILayoutManager CreateLayoutManager() => this;

        // A background takes the space it is given, not the size of its canvas, which follows that space.
        Size ILayoutManager.Measure(double widthConstraint, double heightConstraint)
        {
            var size = new Size(double.IsFinite(widthConstraint) ? widthConstraint : 0, double.IsFinite(heightConstraint) ? heightConstraint : 0);
            var canvas = CanvasSize(size);
            ((IView)_canvas).Measure(canvas.Width, canvas.Height);
            return size;
        }

        Size ILayoutManager.ArrangeChildren(Rect bounds)
        {
            var scale = RenderScale();
            if (_canvas.Scale != scale)
                _canvas.Scale = scale;

            ((IView)_canvas).Arrange(new Rect(Point.Zero, CanvasSize(bounds.Size)));
            return bounds.Size;
        }

        private static Size CanvasSize(Size size) => new(Math.Ceiling(size.Width / RenderScale()) + 1, Math.Ceiling(size.Height / RenderScale()) + 1);

        private static double RenderScale() => PointsPerPixel * (DeviceDisplay.MainDisplayInfo.Density is > 0 and var density ? density : 1);
    }
}

/// <summary>Reads <c>"#FF5E3A, #FFCC00, Teal"</c> (hex or named colours, separated by commas or semicolons) as a list of colours.</summary>
public sealed class MeshColorsTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        var text = value as string ?? throw new NotSupportedException($"Cannot convert {value?.GetType().Name ?? "null"} into a list of colours.");

        return text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => Color.TryParse(token, out var color)
                ? color
                : throw new FormatException($"MeshBackground.Colors: \"{token}\" in \"{text}\" is not a colour."))
            .ToArray();
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        value is IEnumerable<Color> colors ? string.Join(", ", colors.Select(c => c.ToArgbHex(includeAlpha: true))) : null;
}
