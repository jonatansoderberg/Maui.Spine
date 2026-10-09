using Plugin.Maui.Spine.Controls.Avatar.Core;
using Plugin.Maui.Spine.Controls.Avatar.Skia;
using Plugin.Maui.Spine.Controls.Avatar.ThreeD;

namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>
/// A live avatar from a <c>.spineavatar</c> file: state, expression, gestures and speech drawn by the
/// renderer its representation names. The view owns its scheduler and surface; it never owns a
/// voice session or audio player, it only borrows their clock and levels.
/// </summary>
/// <remarks>
/// It redraws at most <see cref="MaxFramesPerSecond"/> times a second, and not at all while detached,
/// hidden, off screen or in the background. A new <see cref="Source"/> cancels the load before it,
/// and the avatar on screen stays until the new one is ready.
/// </remarks>
public sealed class AvatarView : ContentView
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(AvatarSource), typeof(AvatarView),
        propertyChanged: static (b, _, _) => ((AvatarView)b).OnSourceChanged());

    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State), typeof(AvatarState), typeof(AvatarView), AvatarState.Idle,
        propertyChanged: static (b, _, n) => ((AvatarView)b).OnStateChanged((AvatarState)n));

    public static readonly BindableProperty IsMutedProperty = BindableProperty.Create(
        nameof(IsMuted), typeof(bool), typeof(AvatarView), false,
        propertyChanged: static (b, _, n) => ((AvatarView)b).Scheduler?.SetMicMuted((bool)n));

    public static readonly BindableProperty ExpressionProperty = BindableProperty.Create(
        nameof(Expression), typeof(AvatarExpression), typeof(AvatarView), AvatarExpression.Neutral,
        propertyChanged: static (b, _, _) => ((AvatarView)b).ApplyBaseExpression());

    public static readonly BindableProperty ExpressionIntensityProperty = BindableProperty.Create(
        nameof(ExpressionIntensity), typeof(float), typeof(AvatarView), 0.65f,
        coerceValue: static (_, v) => float.IsFinite((float)v) ? Math.Clamp((float)v, 0, 1) : 0.65f,
        propertyChanged: static (b, _, _) => ((AvatarView)b).ApplyBaseExpression());

    public static readonly BindableProperty MotionModeProperty = BindableProperty.Create(
        nameof(MotionMode), typeof(AvatarMotionMode), typeof(AvatarView), AvatarMotionMode.System,
        propertyChanged: static (b, _, _) => ((AvatarView)b).UpdateLoop());

    public static readonly BindableProperty IsAnimationEnabledProperty = BindableProperty.Create(
        nameof(IsAnimationEnabled), typeof(bool), typeof(AvatarView), true,
        propertyChanged: static (b, _, n) => ((AvatarView)b).Scheduler?.SetAnimationEnabled((bool)n));

    public static readonly BindableProperty MaxFramesPerSecondProperty = BindableProperty.Create(
        nameof(MaxFramesPerSecond), typeof(int), typeof(AvatarView), 60,
        coerceValue: static (_, v) => (int)v <= 15 ? 15 : (int)v <= 30 ? 30 : 60,
        propertyChanged: static (b, _, _) => ((AvatarView)b).OnFrameRateChanged());

    public static readonly BindableProperty AccentColorProperty = BindableProperty.Create(
        nameof(AccentColor), typeof(Color), typeof(AvatarView));

    public static readonly BindableProperty AccessibilityTextProperty = BindableProperty.Create(
        nameof(AccessibilityText), typeof(string), typeof(AvatarView),
        propertyChanged: static (b, _, _) => ((AvatarView)b).UpdateDescription());

    /// <summary>Draws the leader's frames with this view's own renderer, for previews at other sizes.</summary>
    public static readonly BindableProperty MirrorOfProperty = BindableProperty.Create(
        nameof(MirrorOf), typeof(AvatarView), typeof(AvatarView),
        propertyChanged: static (b, o, n) => ((AvatarView)b).OnMirrorOfChanged(o as AvatarView, n as AvatarView));

    private readonly List<AvatarView> _mirrors = [];
    private IAvatarSurface? _surface;
    private IDispatcherTimer? _timer;
    private Window? _window;
    private bool _windowStopped;
    private CancellationTokenSource? _loadCancellation;
    private long _loadGeneration;
    private int _seed;

    public AvatarView()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        HandlerChanged += (_, _) =>
        {
            if (Handler is null)
                Detach();
        };
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => RenderNow();
    }

    public AvatarSource? Source
    {
        get => (AvatarSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>The manual state. A voice feed, once connected, sets it instead.</summary>
    public AvatarState State
    {
        get => (AvatarState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The microphone-off indicator; independent of <see cref="State"/>, and it mutes nothing.</summary>
    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    public AvatarExpression Expression
    {
        get => (AvatarExpression)GetValue(ExpressionProperty);
        set => SetValue(ExpressionProperty, value);
    }

    public float ExpressionIntensity
    {
        get => (float)GetValue(ExpressionIntensityProperty);
        set => SetValue(ExpressionIntensityProperty, value);
    }

    public AvatarMotionMode MotionMode
    {
        get => (AvatarMotionMode)GetValue(MotionModeProperty);
        set => SetValue(MotionModeProperty, value);
    }

    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    /// <summary>15, 30 or 60. A ceiling, not a promise: the device may draw fewer.</summary>
    public int MaxFramesPerSecond
    {
        get => (int)GetValue(MaxFramesPerSecondProperty);
        set => SetValue(MaxFramesPerSecondProperty, value);
    }

    /// <summary>Replaces the avatar's <c>accent</c> theme slot; null keeps the avatar's own.</summary>
    public Color? AccentColor
    {
        get => (Color?)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public string? AccessibilityText
    {
        get => (string?)GetValue(AccessibilityTextProperty);
        set => SetValue(AccessibilityTextProperty, value);
    }

    public AvatarView? MirrorOf
    {
        get => (AvatarView?)GetValue(MirrorOfProperty);
        set => SetValue(MirrorOfProperty, value);
    }

    /// <summary>The seed of the next load's scheduler; the same seed gives the same blinks and idle choices.</summary>
    public int Seed
    {
        get => _seed;
        set
        {
            _seed = value;
            Scheduler?.Reseed(value);
        }
    }

    /// <summary>How much spring motion (squash and stretch, tilt, lift) to add: 0 off, 1 as designed, up to 2.</summary>
    public float SecondaryMotion
    {
        get => _secondaryMotion;
        set
        {
            _secondaryMotion = value;
            if (Scheduler is not null)
                Scheduler.SecondaryMotion = value;
        }
    }

    /// <summary>For 3D avatars: "studio" lighting (environment, rim, tone mapping, bloom, contact shadow) or "basic".</summary>
    public string ThreeDLook
    {
        get => _threeDLook;
        set
        {
            _threeDLook = value;
            if (_surface is ThreeDAvatarSurface surface)
                surface.Look = value;
            foreach (var mirror in _mirrors)
                mirror.ThreeDLook = value;
        }
    }

    /// <summary>
    /// For 3D avatars: "native" (SceneKit on iOS and Mac; elsewhere the web surface until a native one exists)
    /// or "web" (three.js in a HybridWebView). Applies from the next load.
    /// </summary>
    public string ThreeDRenderer { get; set; } = "native";

    private IAvatarSurface CreateThreeDSurface(AvatarPackage package, AvatarRepresentation representation)
    {
#if IOS || MACCATALYST
        if ((MirrorOf?.ThreeDRenderer ?? ThreeDRenderer) == "native")
            return new SceneKitAvatarSurface(package, representation);
#endif
        return new ThreeDAvatarSurface(package, representation) { Look = MirrorOf?.ThreeDLook ?? _threeDLook };
    }

    private float _secondaryMotion = 1;
    private string _threeDLook = "studio";

    public AvatarLoadState LoadState { get; private set; }

    /// <summary>When the current load began (<see cref="System.Diagnostics.Stopwatch"/> ticks); with <see cref="AvatarFrameStats.FirstFrameTimestamp"/> it gives the time to first frame.</summary>
    public long LoadStartedTimestamp { get; private set; }

    /// <summary>Reading, verifying and compiling the archive, in milliseconds.</summary>
    public double LoadMilliseconds { get; private set; }

    public Exception? LoadError { get; private set; }

    public AvatarPackage? Package { get; private set; }

    public AvatarRepresentation? Representation { get; private set; }

    public AvatarScheduler? Scheduler { get; private set; }

    public string? RendererName => _surface?.RendererName;

    public AvatarFrameStats? Stats => _surface?.Stats;

    public string? RendererDetail => _surface?.Detail;

    /// <summary>The last frame shown; for an inspector.</summary>
    public AvatarRenderFrame? LastFrame { get; private set; }

    public event EventHandler? AvatarLoaded;

    public event EventHandler<Exception>? AvatarFailed;

    /// <summary>Raised after each frame is handed to the surface, on the UI thread.</summary>
    public event EventHandler<AvatarRenderFrame>? FrameRendered;

    public void SetExpression(AvatarExpressionRequest request) => RequireScheduler().SetExpression(request);

    public Task PlayGestureAsync(string name, CancellationToken cancellationToken = default) =>
        Task.Delay(RequireScheduler().PlayGesture(name), cancellationToken);

    /// <summary>Barge-in: speech closes at once and the interrupt reflex plays; <see cref="State"/> follows the scheduler.</summary>
    public void Interrupt() => RequireScheduler().Interrupt();

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
        LoadStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = cancellation;
        LoadState = AvatarLoadState.Loading;
        OnPropertyChanged(nameof(LoadState));

        try
        {
            var (package, representation, model) = await Task.Run(async () =>
            {
                await using var stream = await source.OpenAsync(cancellation.Token);
                var package = AvatarArchive.Read(stream, cancellationToken: cancellation.Token);
                var representation = package.SelectRepresentation(Renderers)
                    ?? throw new NotSupportedException($"{package.Manifest.DisplayName} has no representation this app can draw: it offers {string.Join(", ", package.Manifest.Representations.Select(r => r.Renderer))}, the app has {string.Join(", ", Renderers)}.");
                var model = representation.Renderer == "skia" ? Spine2dModel.Compile(package, representation) : null;
                return (package, representation, model);
            }, cancellation.Token);

            if (generation != _loadGeneration)
                return;

            LoadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(LoadStartedTimestamp).TotalMilliseconds;
            Show(package, representation, model);
            LoadError = null;
            LoadState = AvatarLoadState.Loaded;
            OnPropertyChanged(nameof(LoadState));
            AvatarLoaded?.Invoke(this, EventArgs.Empty);
            foreach (var mirror in _mirrors)
                mirror.FollowLeader();
        }
        catch (OperationCanceledException) when (generation != _loadGeneration || cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e) when (generation == _loadGeneration)
        {
            // The avatar on screen, if any, stays; the error is there to read, never swallowed.
            LoadError = e;
            LoadState = AvatarLoadState.Failed;
            OnPropertyChanged(nameof(LoadState));
            AvatarFailed?.Invoke(this, e);
        }
        finally
        {
            // Cleared before disposal: the next load cancels this one through the field.
            if (_loadCancellation == cancellation)
                _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    private static readonly string[] Renderers = ["skia", "native3d"];

    private void Show(AvatarPackage package, AvatarRepresentation representation, Spine2dModel? model)
    {
        var old = _surface;
        _surface = model is not null ? new SkiaAvatarSurface(model) : CreateThreeDSurface(package, representation);
        Content = _surface.View;
        old?.Dispose();

        Package = package;
        Representation = representation;
        if (MirrorOf is null)
        {
            Scheduler = new AvatarScheduler(package, representation, seed: _seed);
            Scheduler.SetState(State);
            Scheduler.SetMicMuted(IsMuted);
            Scheduler.SetAnimationEnabled(IsAnimationEnabled);
            Scheduler.SecondaryMotion = _secondaryMotion;
            ApplyBaseExpression();
        }
        UpdateDescription();
        UpdateLoop();
        RenderNow();
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
        Scheduler = null;
        LoadState = AvatarLoadState.Empty;
        OnPropertyChanged(nameof(LoadState));
    }

    private AvatarScheduler RequireScheduler() =>
        Scheduler ?? throw new InvalidOperationException(LoadState == AvatarLoadState.Failed
            ? $"The avatar failed to load: {LoadError?.Message}"
            : "No avatar is loaded yet; await LoadAsync first.");

    private void OnSourceChanged()
    {
        if (MirrorOf is null)
            _ = LoadAsync();
    }

    private void OnStateChanged(AvatarState state)
    {
        Scheduler?.SetState(state);
        UpdateDescription();
    }

    private void ApplyBaseExpression() => Scheduler?.SetBaseExpression(Expression, ExpressionIntensity);

    private void OnMirrorOfChanged(AvatarView? oldLeader, AvatarView? newLeader)
    {
        oldLeader?._mirrors.Remove(this);
        if (newLeader is null)
            return;
        newLeader._mirrors.Add(this);
        FollowLeader();
    }

    private void FollowLeader()
    {
        if (MirrorOf is { Package: { } package, Representation: { } representation })
            Show(package, representation, representation.Renderer == "skia" ? Spine2dModel.Compile(package, representation) : null);
    }

    private void UpdateDescription()
    {
        var name = Package?.Manifest.DisplayName ?? "Avatar";
        SemanticProperties.SetDescription(this, AccessibilityText ?? $"{name}, {AvatarVocabulary.Name(State)}{(IsMuted ? ", microphone off" : "")}");
    }

    private bool ReducedMotion => MotionMode == AvatarMotionMode.Reduced || (MotionMode == AvatarMotionMode.System && ReduceMotion.IsEnabled);

    private void Attach()
    {
        if (_window is null && Window is { } window)
        {
            _window = window;
            window.Stopped += OnWindowStopped;
            window.Resumed += OnWindowResumed;
            window.Activated += OnWindowActivated;
        }
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

    // Reduce Motion is changed in the system's settings, so it is read again whenever the app comes back.
    private void OnWindowActivated(object? sender, EventArgs e) => UpdateLoop();

    private void UpdateLoop()
    {
        Scheduler?.SetReducedMotion(ReducedMotion);

        // A mirror draws when its leader ticks; only a leader runs a loop.
        var run = MirrorOf is null && Scheduler is not null && _window is not null && !_windowStopped && IsVisible;
        if (run)
            StartTimer();
        else
            StopTimer();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IsVisibleProperty.PropertyName)
            UpdateLoop();
    }

    private void OnFrameRateChanged()
    {
        if (_timer is not null)
            _timer.Interval = TimeSpan.FromSeconds(1.0 / MaxFramesPerSecond);
    }

    private void StartTimer()
    {
        if (_timer is not null || Dispatcher is null)
            return;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.0 / MaxFramesPerSecond);
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
    }

    private void OnTick(object? sender, EventArgs e) => RenderNow();

    private void RenderNow()
    {
        if (Scheduler is null || _surface is null)
            return;

        var frame = Scheduler.Update();
        // The scheduler moves on by itself after an interrupt; the bindable state follows it.
        if (Scheduler.State != State)
            SetValue(StateProperty, Scheduler.State);
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        LastFrame = frame;
        if (IsShown())
            _surface.Render(frame, dark, AccentColor);
        foreach (var mirror in _mirrors)
        {
            if (mirror._surface is not null && mirror.IsShown())
                mirror._surface.Render(frame, dark, mirror.AccentColor ?? AccentColor);
        }
        FrameRendered?.Invoke(this, frame);
    }

    private bool IsShown()
    {
        for (Element? element = this; element is not null; element = element.Parent)
        {
            if (element is VisualElement { IsVisible: false })
                return false;
        }
        return Width > 0 && Height > 0;
    }
}
