using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Voice;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A voice conversation drawn as it happens: the levels, the 24 bands each way and the state of an
/// <see cref="IVoiceSession"/> (or a bare <see cref="IVoiceAudio"/>), through a named visual.
/// </summary>
/// <remarks>
/// <para>The built-in visuals are <c>orb</c> (the default), <c>bars</c>, <c>ring</c> and <c>avatar</c>; an app adds its own
/// with <c>UseSpineVoiceView(o =&gt; o.AddVisual&lt;T&gt;(name))</c>.</para>
/// <para>It redraws at most <see cref="FrameRate"/> times a second, and not at all while it is detached, hidden (itself
/// or an ancestor), scrolled out of sight or the app is in the background. Under Reduce Motion it draws four times a second
/// and the visuals show the state and a slow level instead of the spectrum. Screen readers hear one element whose
/// description is the state; muting, an interruption and the end are announced, not each turn.</para>
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <VoiceView Session="{Binding Voice}" Kind="orb" HeightRequest="240" />
/// ]]></code>
/// </example>
public sealed class VoiceView : ContentView
{
    private const int ReducedFrameRate = 4;

    /// <summary>The bindable property for <see cref="Session"/>.</summary>
    public static readonly BindableProperty SessionProperty = BindableProperty.Create(
        nameof(Session), typeof(IVoiceSession), typeof(VoiceView), propertyChanged: static (b, _, _) => ((VoiceView)b).Update());

    /// <summary>The bindable property for <see cref="Audio"/>.</summary>
    public static readonly BindableProperty AudioProperty = BindableProperty.Create(
        nameof(Audio), typeof(IVoiceAudio), typeof(VoiceView), propertyChanged: static (b, _, _) => ((VoiceView)b).Update());

    /// <summary>The bindable property for <see cref="Kind"/>.</summary>
    public static readonly BindableProperty KindProperty = BindableProperty.Create(
        nameof(Kind), typeof(string), typeof(VoiceView), VoiceVisualRegistry.Default, propertyChanged: static (b, _, _) => ((VoiceView)b).OnVisualChanged());

    /// <summary>The bindable property for <see cref="FrameRate"/>.</summary>
    public static readonly BindableProperty FrameRateProperty = BindableProperty.Create(
        nameof(FrameRate), typeof(int), typeof(VoiceView), 30,
        coerceValue: static (_, value) => Math.Clamp((int)value, 1, 60),
        propertyChanged: static (b, _, _) => ((VoiceView)b).Update());

    /// <summary>The bindable property for <see cref="InputColor"/>.</summary>
    public static readonly BindableProperty InputColorProperty = BindableProperty.Create(
        nameof(InputColor), typeof(Color), typeof(VoiceView));

    /// <summary>The bindable property for <see cref="OutputColor"/>.</summary>
    public static readonly BindableProperty OutputColorProperty = BindableProperty.Create(
        nameof(OutputColor), typeof(Color), typeof(VoiceView));

    private static readonly Color WarmDefault = Color.FromArgb("#FF7A45");

    private readonly SKCanvasView _canvas;
    private readonly Stopwatch _clock = new();
    private VoiceVisual? _visual;
    private VoiceVisualView? _hosted;
    private VoiceFrame? _frame;
    private VoiceState _announced = VoiceState.Idle;
    private bool _unknownKind;
    private Color? _accent;
    private FrameTimer? _timer;
    private Window? _window;
    private bool _windowStopped;
    private TimeSpan _lastTick;
#if ANDROID
    private Android.Graphics.Rect? _visibleRect;
#endif

    /// <summary>Creates the view, drawing <c>orb</c> until <see cref="Kind"/> says otherwise.</summary>
    public VoiceView()
    {
        _canvas = new SKCanvasView { InputTransparent = true, EnableTouchEvents = false };
        _canvas.PaintSurface += OnPaintSurface;
        SemanticProperties.SetDescription(this, Describe(VoiceState.Idle));
        OnVisualChanged();

        SpineTheme.Track(this, () => _accent = null);
        HandlerChanged += (_, _) => WarnUnknownKind();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
                Update();
        };
    }

    /// <summary>The conversation to draw. Its frame carries the conversation's state (listening, thinking, speaking).</summary>
    public IVoiceSession? Session
    {
        get => (IVoiceSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>An engine to draw instead of a session, for a page that drives the audio itself.</summary>
    public IVoiceAudio? Audio
    {
        get => (IVoiceAudio?)GetValue(AudioProperty);
        set => SetValue(AudioProperty, value);
    }

    /// <summary>The visual's name: <c>orb</c> (default), <c>bars</c>, <c>ring</c>, <c>avatar</c> or one the app added.</summary>
    public string? Kind
    {
        get => (string?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Frames a second, 1–60. Default 30.</summary>
    public int FrameRate
    {
        get => (int)GetValue(FrameRateProperty);
        set => SetValue(FrameRateProperty, value);
    }

    /// <summary>The user's colour. Unset, the app's accent (<see cref="SpineTheme.GetAccent"/>), following its changes.</summary>
    public Color? InputColor
    {
        get => (Color?)GetValue(InputColorProperty);
        set => SetValue(InputColorProperty, value);
    }

    /// <summary>The model's colour. Unset, a warm orange.</summary>
    public Color? OutputColor
    {
        get => (Color?)GetValue(OutputColorProperty);
        set => SetValue(OutputColorProperty, value);
    }

    private void OnVisualChanged()
    {
        var created = SpineVoiceViewExtensions.Options.Registry.Create(Kind, out var known);
        _unknownKind = !known;
        WarnUnknownKind();

        _visual = created as VoiceVisual;
        _hosted = created as VoiceVisualView;
        Content = _hosted is not null ? _hosted : _canvas;
        _canvas.InvalidateSurface();
    }

    // XAML sets Kind before the handler exists, so the warning waits for it.
    private void WarnUnknownKind()
    {
        if (!_unknownKind || Handler?.MauiContext?.Services.GetService<ILoggerFactory>() is not { } loggers)
            return;
        _unknownKind = false;
        loggers.CreateLogger("Plugin.Maui.Spine.Controls.Voice").LogWarning(
            "VoiceView: no visual is named '{Visual}'; drawing '{Default}'. Register it with UseSpineVoiceView(o => o.AddVisual<T>(name)).",
            Kind, VoiceVisualRegistry.Default);
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
        ReducedMotion.Changed += OnReducedMotionChanged;
        Update();
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
        ReducedMotion.Changed -= OnReducedMotionChanged;
        _windowStopped = false;
        StopTimer();
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        _windowStopped = true;
        Update();
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        _windowStopped = false;
        Update();
    }

    private void OnWindowActivated(object? sender, EventArgs e) => Update();

    private void OnReducedMotionChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(Update);

    private void Update()
    {
        var run = (Session is not null || Audio is not null) && _window is not null && !_windowStopped && IsVisible;
        if (!run)
        {
            StopTimer();
            return;
        }

        var rate = ReducedMotion.IsOn ? ReducedFrameRate : FrameRate;
        if (_timer is null)
        {
            _timer = new FrameTimer();
            _timer.Tick += OnTick;
            _clock.Restart();
            _lastTick = TimeSpan.Zero;
        }
        _timer.Interval = TimeSpan.FromSeconds(1.0 / rate);
        if (!_timer.IsRunning)
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
        if (!IsShown())
            return;

        _frame = Session?.UpdateFrame() ?? Audio?.UpdateFrame();
        if (_frame is null)
            return;

        if (_frame.State != _announced)
        {
            _announced = _frame.State;
            var description = Describe(_announced);
            SemanticProperties.SetDescription(this, description);
            // Not each turn: "Speaking" and "Listening" read out at every turn would talk over the answer.
            if (_announced is VoiceState.Muted or VoiceState.Interrupted or VoiceState.Idle)
                SemanticScreenReader.Announce(description);
        }

        if (_hosted is not null)
            _hosted.OnFrame(_frame, Context(1));
        else
            _canvas.InvalidateSurface();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_visual is null || _frame is null)
            return;

        var scale = _canvas.Width > 0 ? (float)(e.Info.Width / _canvas.Width) : 1f;
        _visual.Draw(canvas, new SKRect(0, 0, e.Info.Width, e.Info.Height), _frame, Context(scale));
    }

    private VoiceVisualContext Context(float scale)
    {
        // The clock rather than a count of ticks, capped so a stall does not make a visual jump.
        var now = _clock.Elapsed;
        var delta = now - _lastTick;
        _lastTick = now;
        if (delta > TimeSpan.FromMilliseconds(250))
            delta = TimeSpan.FromMilliseconds(250);

        var input = InputColor ?? (_accent ??= SpineTheme.GetAccent(Application.Current?.RequestedTheme ?? AppTheme.Light) ?? Colors.DodgerBlue);
        var output = OutputColor ?? WarmDefault;
        return new VoiceVisualContext(input.ToSKColor(), output.ToSKColor(), scale, delta, ReducedMotion.IsOn);
    }

    /// <summary>
    /// Whether the view is on screen. A hidden ancestor (an unselected tab, a collapsed panel) or a scroll view that moved
    /// it out of sight hides it without telling it.
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

    private static string Describe(VoiceState state) => state switch
    {
        VoiceState.Connecting => "Connecting",
        VoiceState.Listening => "Listening",
        VoiceState.Thinking => "Thinking",
        VoiceState.Speaking => "Speaking",
        VoiceState.Interrupted => "Interrupted",
        VoiceState.Muted => "Muted",
        _ => "Not connected",
    };
}
