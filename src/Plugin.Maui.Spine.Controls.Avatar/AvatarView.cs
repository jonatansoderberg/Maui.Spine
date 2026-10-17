using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Controls.Avatar.Skia;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A live avatar from a <c>.spineavatar</c>: state, expression, gestures and speech, drawn by the renderer its best
/// representation names (Skia for vector scenes and shaders, SceneKit for glTF on iOS and Mac Catalyst), or as its poster
/// where the platform has none.
/// </summary>
/// <remarks>
/// <para>With no <see cref="Driver"/>, the view's own properties drive it (<see cref="State"/>, <see cref="IsMuted"/>,
/// <see cref="Expression"/>). A driver (<see cref="TextAvatarDriver"/>, a speech or voice driver) takes over the state, the
/// levels and the mouth, and <see cref="State"/> follows it.</para>
/// <para>It redraws at most <see cref="MaxFramesPerSecond"/> times a second, and not at all while it is detached, hidden
/// (itself or an ancestor), scrolled out of sight or the app is in the background. Under Reduce Motion
/// (<see cref="MotionMode"/>) it holds a still pose, draws four times a second and keeps a slow mouth.</para>
/// <para>Screen readers hear one element: the avatar's name and state, or <see cref="AccessibilityText"/>. Muting and an
/// interruption are announced; other state changes are not, so they do not talk over a conversation.</para>
/// <para>A new <see cref="Source"/> cancels the load before it; the avatar on screen stays until the new one is ready.</para>
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <AvatarView Source="{Binding Avatar}" State="Listening" Expression="Curious" HeightRequest="240" />
/// ]]></code>
/// </example>
public sealed class AvatarView : ContentView
{
    private const int ReducedFrameRate = 4;

    /// <summary>The bindable property for <see cref="Source"/>.</summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(AvatarSource), typeof(AvatarView),
        propertyChanged: static (b, _, _) => _ = ((AvatarView)b).LoadAsync());

    /// <summary>The bindable property for <see cref="Driver"/>.</summary>
    public static readonly BindableProperty DriverProperty = BindableProperty.Create(
        nameof(Driver), typeof(AvatarDriver), typeof(AvatarView),
        propertyChanged: static (b, _, n) => ((AvatarView)b).OnDriverChanged((AvatarDriver?)n));

    /// <summary>The bindable property for <see cref="State"/>.</summary>
    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State), typeof(AvatarState), typeof(AvatarView), AvatarState.Idle, BindingMode.TwoWay,
        propertyChanged: static (b, o, n) => ((AvatarView)b).OnStateChanged((AvatarState)o, (AvatarState)n));

    /// <summary>The bindable property for <see cref="IsMuted"/>.</summary>
    public static readonly BindableProperty IsMutedProperty = BindableProperty.Create(
        nameof(IsMuted), typeof(bool), typeof(AvatarView), false,
        propertyChanged: static (b, _, n) => ((AvatarView)b).OnMutedChanged((bool)n));

    /// <summary>The bindable property for <see cref="Expression"/>.</summary>
    public static readonly BindableProperty ExpressionProperty = BindableProperty.Create(
        nameof(Expression), typeof(AvatarExpression), typeof(AvatarView), AvatarExpression.Neutral,
        propertyChanged: static (b, _, _) => ((AvatarView)b).ApplyBaseExpression());

    /// <summary>The bindable property for <see cref="ExpressionIntensity"/>.</summary>
    public static readonly BindableProperty ExpressionIntensityProperty = BindableProperty.Create(
        nameof(ExpressionIntensity), typeof(float), typeof(AvatarView), 0.65f,
        coerceValue: static (_, v) => float.IsFinite((float)v) ? Math.Clamp((float)v, 0, 1) : 0.65f,
        propertyChanged: static (b, _, _) => ((AvatarView)b).ApplyBaseExpression());

    /// <summary>The bindable property for <see cref="MotionMode"/>.</summary>
    public static readonly BindableProperty MotionModeProperty = BindableProperty.Create(
        nameof(MotionMode), typeof(AvatarMotionMode), typeof(AvatarView), AvatarMotionMode.System,
        propertyChanged: static (b, _, _) => ((AvatarView)b).UpdateLoop());

    /// <summary>The bindable property for <see cref="MaxFramesPerSecond"/>.</summary>
    public static readonly BindableProperty MaxFramesPerSecondProperty = BindableProperty.Create(
        nameof(MaxFramesPerSecond), typeof(int), typeof(AvatarView), 30,
        coerceValue: static (_, v) => (int)v <= 15 ? 15 : (int)v <= 30 ? 30 : 60,
        propertyChanged: static (b, _, _) => ((AvatarView)b).UpdateLoop());

    /// <summary>The bindable property for <see cref="AccentColor"/>.</summary>
    public static readonly BindableProperty AccentColorProperty = BindableProperty.Create(
        nameof(AccentColor), typeof(Color), typeof(AvatarView),
        propertyChanged: static (b, _, _) => ((AvatarView)b).OnThemeChanged());

    /// <summary>The bindable property for <see cref="AccessibilityText"/>.</summary>
    public static readonly BindableProperty AccessibilityTextProperty = BindableProperty.Create(
        nameof(AccessibilityText), typeof(string), typeof(AvatarView),
        propertyChanged: static (b, _, _) => ((AvatarView)b).UpdateDescription());

    private static readonly HashSet<string> s_warnedPosters = new(StringComparer.Ordinal);

    private readonly AvatarRuntime _runtime = new();
    private readonly Stopwatch _clock = new();
    private AvatarSurface? _surface;
    private IDispatcherTimer? _timer;
    private Window? _window;
    private bool _windowStopped;
    private CancellationTokenSource? _loadCancellation;
    private long _loadGeneration;
    private TimeSpan _lastTick;
    private Color? _themeAccent;
    private bool _themeAccentRead;
    private bool _syncingState;
    private (float X, float Y)? _lookAt;
#if ANDROID
    private Android.Graphics.Rect? _visibleRect;
#endif

    /// <summary>Creates an empty view; set <see cref="Source"/> to show an avatar.</summary>
    public AvatarView()
    {
        UpdateDescription();
        SpineTheme.Track(this, OnThemeChanged);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
                UpdateLoop();
        };
    }

    /// <summary>The avatar to show.</summary>
    public AvatarSource? Source
    {
        get => (AvatarSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// What drives the avatar. Null: the view's own <see cref="State"/>, <see cref="IsMuted"/> and <see cref="Expression"/>;
    /// removing a driver leaves the avatar Idle. A driver drives one view at a time.
    /// </summary>
    public AvatarDriver? Driver
    {
        get => (AvatarDriver?)GetValue(DriverProperty);
        set => SetValue(DriverProperty, value);
    }

    /// <summary>What the avatar is doing. Set it to drive the avatar by hand; with a <see cref="Driver"/> it follows the driver.</summary>
    public AvatarState State
    {
        get => (AvatarState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>Shows the microphone-off badge, whatever the state; it mutes nothing.</summary>
    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    /// <summary>The expression shown when no timed one is (<see cref="SetExpression"/>).</summary>
    public AvatarExpression Expression
    {
        get => (AvatarExpression)GetValue(ExpressionProperty);
        set => SetValue(ExpressionProperty, value);
    }

    /// <summary>How strongly <see cref="Expression"/> shows, 0–1. Default 0.65.</summary>
    public float ExpressionIntensity
    {
        get => (float)GetValue(ExpressionIntensityProperty);
        set => SetValue(ExpressionIntensityProperty, value);
    }

    /// <summary>System (default) follows the platform's Reduce Motion; Reduced and Full override it.</summary>
    public AvatarMotionMode MotionMode
    {
        get => (AvatarMotionMode)GetValue(MotionModeProperty);
        set => SetValue(MotionModeProperty, value);
    }

    /// <summary>15, 30 (default) or 60. A ceiling, not a promise: the device may draw fewer.</summary>
    public int MaxFramesPerSecond
    {
        get => (int)GetValue(MaxFramesPerSecondProperty);
        set => SetValue(MaxFramesPerSecondProperty, value);
    }

    /// <summary>The colour of the avatar's <c>accent</c> slot. Unset, the app's accent (<see cref="SpineTheme.GetAccent"/>), or the avatar's own when the app has none.</summary>
    public Color? AccentColor
    {
        get => (Color?)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    /// <summary>What a screen reader says for the avatar. Unset, its name and state.</summary>
    public string? AccessibilityText
    {
        get => (string?)GetValue(AccessibilityTextProperty);
        set => SetValue(AccessibilityTextProperty, value);
    }

    /// <summary>Where the view is with its <see cref="Source"/>.</summary>
    public AvatarLoadState LoadState { get; private set; }

    /// <summary>Why the last load failed, or null.</summary>
    public Exception? LoadError { get; private set; }

    /// <summary>The avatar shown, or null. Raises <see cref="BindableObject.PropertyChanged"/>, as do <see cref="Representation"/>, <see cref="RendererName"/>, <see cref="Gestures"/> and <see cref="LipSyncQuality"/>.</summary>
    public AvatarPackage? Package { get; private set; }

    /// <summary>The representation drawn, or null when none is (empty, or shown as its poster).</summary>
    public AvatarRepresentation? Representation { get; private set; }

    /// <summary>The renderer drawing it, such as <c>skia (Metal)</c>, <c>native3d (SceneKit)</c> or <c>poster</c>.</summary>
    public string? RendererName => _surface?.RendererName;

    /// <summary>What the mouth timing is worth now: the driver's, or None.</summary>
    public AvatarLipSyncQuality LipSyncQuality => _runtime.Input.LipSyncQuality;

    /// <summary>The gestures the avatar can play (nod, shake, lean…).</summary>
    public IReadOnlyList<string> Gestures => [.. _runtime.Input.Gestures];

    /// <summary>Raised on the UI thread when an avatar is shown.</summary>
    public event EventHandler? AvatarLoaded;

    /// <summary>Raised on the UI thread when a load fails; the avatar before it stays.</summary>
    public event EventHandler<Exception>? AvatarFailed;

    /// <summary>The runtime: the driver, the input and the scheduler. A host that runs its own tick drives it.</summary>
    internal AvatarRuntime Runtime => _runtime;

    /// <summary>The surface drawing the avatar, for an inspector.</summary>
    internal AvatarSurface? Surface => _surface;

    /// <summary>Set by a host (VoiceView's character) that calls <see cref="TickFromHost"/> from its own tick: the view runs no timer.</summary>
    internal bool IsHosted
    {
        get;
        set
        {
            field = value;
            UpdateLoop();
        }
    }

    /// <summary>Shows <paramref name="request"/>'s expression for its duration, then <see cref="Expression"/> again.</summary>
    public void SetExpression(AvatarExpressionRequest request) => _runtime.Input.SetExpression(request);

    /// <summary>Plays a gesture; completes when it ends, at once when the avatar has no such gesture.</summary>
    public Task PlayGestureAsync(string name, CancellationToken cancellationToken = default) =>
        Task.Delay(_runtime.Input.PlayGesture(name), cancellationToken);

    /// <summary>Cuts speech short: the mouth closes at once, the interrupted reflex plays, and the driver stops speaking.</summary>
    public void Interrupt() => _runtime.Interrupt();

    /// <summary>
    /// Looks toward a point in this view's coordinates (it may lie outside the view, at a control elsewhere on the page), or
    /// back at the viewer with null. The direction assumes the viewer sits about one view-width away.
    /// </summary>
    public void LookAt(Point? point)
    {
        _lookAt = point is { } p && Width > 0
            ? ((float)Math.Atan2(p.X - Width / 2, Width), (float)Math.Atan2(Height / 2 - p.Y, Width))
            : null;
        _runtime.Input.LookAt(_lookAt?.X, _lookAt?.Y);
    }

    /// <summary>Loads <see cref="Source"/> again; setting the source does this by itself.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var source = Source;
        _loadCancellation?.Cancel();
        if (source is null)
        {
            Unload();
            return;
        }

        var generation = ++_loadGeneration;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = cancellation;
        SetLoadState(AvatarLoadState.Loading);

        try
        {
            var renderers = Renderers();
            var (package, representation, renderer, skia) = await Task.Run(async () =>
            {
                await using var stream = await source.OpenAsync(cancellation.Token);
                var package = AvatarArchive.Read(stream, cancellationToken: cancellation.Token);
                foreach (var candidate in package.Manifest.Representations)
                {
                    if (renderers.FirstOrDefault(r => r.CanDraw(candidate)) is { } renderer)
                    {
                        // The Skia model compiles here, off the UI thread; other renderers build on it.
                        var skia = renderer is SkiaAvatarRenderer ? SkiaAvatarModel.Compile(package, candidate) : null;
                        return (package, (AvatarRepresentation?)candidate, (AvatarRenderer?)renderer, skia);
                    }
                }
                return (package, null, null, null);
            }, cancellation.Token);

            if (generation != _loadGeneration)
                return;

            var surface = skia is not null ? new SkiaAvatarSurface(skia)
                : renderer is not null ? renderer.CreateSurface(package, representation!)
                : new PosterAvatarSurface(package);
            if (renderer is null)
                WarnPoster(package);
            Show(package, representation, surface);
            LoadError = null;
            SetLoadState(AvatarLoadState.Loaded);
            AvatarLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (generation != _loadGeneration || cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e) when (generation == _loadGeneration)
        {
            // The avatar on screen, if any, stays; the error is there to read and in the log, never swallowed.
            LoadError = e;
            SetLoadState(AvatarLoadState.Failed);
            Logger()?.LogWarning(e, "AvatarView: {Source} did not load: {Message}", source.Name, e.Message);
            AvatarFailed?.Invoke(this, e);
        }
        finally
        {
            if (_loadCancellation == cancellation)
                _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>One frame, from a host that runs its own tick (<see cref="IsHosted"/>).</summary>
    internal void TickFromHost(TimeSpan delta) => Tick(delta);

    private static List<AvatarRenderer> Renderers()
    {
        var renderers = new List<AvatarRenderer>(SpineAvatarExtensions.Options.Renderers);
        renderers.Add(SkiaAvatarRenderer.Instance);
#if IOS || MACCATALYST
        renderers.Add(SceneKitAvatarRenderer.Instance);
#endif
        return renderers;
    }

    private void Show(AvatarPackage package, AvatarRepresentation? representation, AvatarSurface surface)
    {
        var old = _surface;
        _surface = surface;
        Content = surface.View;
        old?.Dispose();

        Package = package;
        Representation = representation;
        // A poster has no representation to schedule; the input keeps the state for the next avatar.
        if (representation is not null)
            _runtime.Load(new AvatarScheduler(package, representation));
        else
            _runtime.Unload();
        _runtime.Scheduler?.SetReducedMotion(IsReducedMotion);
        ApplyBaseExpression();
        if (surface is PosterAvatarSurface poster)
            poster.State = State;
        UpdateDescription();
        UpdateLoop();
        Tick(TimeSpan.Zero);
        RaiseShown();
    }

    private void RaiseShown()
    {
        OnPropertyChanged(nameof(Package));
        OnPropertyChanged(nameof(Representation));
        OnPropertyChanged(nameof(RendererName));
        OnPropertyChanged(nameof(Gestures));
        OnPropertyChanged(nameof(LipSyncQuality));
    }

    private void Unload()
    {
        _loadGeneration++;
        StopTimer();
        _surface?.Dispose();
        _surface = null;
        Content = null;
        Package = null;
        Representation = null;
        _runtime.Unload();
        SetLoadState(AvatarLoadState.Empty);
        UpdateDescription();
        RaiseShown();
    }

    private void SetLoadState(AvatarLoadState state)
    {
        LoadState = state;
        OnPropertyChanged(nameof(LoadState));
    }

    private void OnDriverChanged(AvatarDriver? driver)
    {
        _runtime.Driver = driver;
        if (driver is null)
        {
            // Back to the view's own properties. State followed the driver, so what it says now is the driver's, not the
            // app's: the avatar rests.
            State = AvatarState.Idle;
            _runtime.Input.SetState(AvatarState.Idle);
            _runtime.Input.SetMuted(IsMuted);
        }
        OnPropertyChanged(nameof(LipSyncQuality));
    }

    private void OnStateChanged(AvatarState old, AvatarState state)
    {
        if (!_syncingState)
            _runtime.Input.SetState(state);
        if (_surface is PosterAvatarSurface poster)
            poster.State = state;
        UpdateDescription();
        if (state == AvatarState.Interrupted && old != state)
            SemanticScreenReader.Announce(Describe());
    }

    private void OnMutedChanged(bool muted)
    {
        _runtime.Input.SetMuted(muted);
        UpdateDescription();
        if (Handler is not null)
            SemanticScreenReader.Announce(muted ? $"{DisplayName}, microphone off" : $"{DisplayName}, microphone on");
    }

    private void ApplyBaseExpression() => _runtime.Input.SetBaseExpression(Expression, ExpressionIntensity);

    private void OnThemeChanged()
    {
        _themeAccentRead = false;
        Tick(TimeSpan.Zero);
    }

    private string DisplayName => Package?.Manifest.DisplayName ?? Source?.Name ?? "Avatar";

    private string Describe() => AccessibilityText ?? $"{DisplayName}, {AvatarVocabulary.Name(State)}{(IsMuted ? ", microphone off" : "")}";

    private void UpdateDescription() => SemanticProperties.SetDescription(this, Describe());

    private bool IsReducedMotion => MotionMode == AvatarMotionMode.Reduced || (MotionMode == AvatarMotionMode.System && ReducedMotion.IsOn);

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
        UpdateLoop();
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
        UpdateLoop();
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        _windowStopped = false;
        UpdateLoop();
    }

    // Reduce Motion is changed in the system's settings; Windows raises no event, so it is read again on activation.
    private void OnWindowActivated(object? sender, EventArgs e) => UpdateLoop();

    private void OnReducedMotionChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(UpdateLoop);

    private void UpdateLoop()
    {
        var reduced = IsReducedMotion;
        _runtime.Scheduler?.SetReducedMotion(reduced);

        var run = !IsHosted && _surface is not null && _window is not null && !_windowStopped && IsVisible;
        if (!run)
        {
            StopTimer();
            return;
        }

        if (_timer is null)
        {
            _timer = Dispatcher.CreateTimer();
            _timer.Tick += OnTick;
            _clock.Restart();
            _lastTick = TimeSpan.Zero;
        }
        _timer.Interval = TimeSpan.FromSeconds(1.0 / (reduced ? ReducedFrameRate : MaxFramesPerSecond));
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
        // The clock rather than the interval, capped so a stall does not make the avatar jump.
        var now = _clock.Elapsed;
        var delta = now - _lastTick;
        _lastTick = now;
        if (delta > TimeSpan.FromMilliseconds(250))
            delta = TimeSpan.FromMilliseconds(250);
        if (IsShown())
            Tick(delta);
    }

    private void Tick(TimeSpan delta)
    {
        if (_surface is null)
            return;

        var quality = _runtime.Input.LipSyncQuality;
        var frame = _runtime.Tick(delta);
        if (_runtime.Input.LipSyncQuality != quality)
            OnPropertyChanged(nameof(LipSyncQuality));

        // The driver, or the scheduler on its way out of Interrupted, moves the state; the property follows.
        var state = _runtime.Input.State;
        if (state != State)
        {
            _syncingState = true;
            try
            {
                SetValue(StateProperty, state);
            }
            finally
            {
                _syncingState = false;
            }
        }

        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        if (!_themeAccentRead)
        {
            _themeAccent = SpineTheme.GetAccent(theme);
            _themeAccentRead = true;
        }
        _surface.Render(frame ?? s_empty, theme == AppTheme.Dark, AccentColor ?? _themeAccent);
    }

    private static readonly AvatarRenderFrame s_empty = new();

    /// <summary>
    /// Whether the view is on screen. A hidden ancestor (an unselected tab, a collapsed panel) or a scroll view that moved
    /// it out of sight hides it without telling it.
    /// </summary>
    private bool IsShown()
    {
        if (Width <= 0 || Height <= 0)
            return false;
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

    private void WarnPoster(AvatarPackage package)
    {
        lock (s_warnedPosters)
        {
            if (!s_warnedPosters.Add(package.Manifest.Id))
                return;
        }
        Logger()?.LogWarning(
            "AvatarView: {Avatar} has no representation this platform can draw (it offers {Renderers}); showing its poster.",
            package.Manifest.Id, string.Join(", ", package.Manifest.Representations.Select(r => $"{r.Renderer}/{r.Format}")));
    }

    private ILogger? Logger() =>
        (Handler?.MauiContext?.Services ?? IPlatformApplication.Current?.Services)?.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Controls.Avatar");
}
