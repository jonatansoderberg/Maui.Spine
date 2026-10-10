using System.Globalization;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Controls.Avatar.Core;
using Plugin.Maui.Spine.Controls.Avatar.Feeds;
using Plugin.Maui.Spine.Controls.Avatar.Skia;

namespace SpineAvatarLab.Lab;

/// <summary>
/// The inspector of spec §23: every state, the mic flag, expressions with intensity and duration,
/// gestures, levels and a band generator, visemes, the fixture timeline, platform TTS, sizes,
/// theme, Reduce Motion, seed and frame rate, with diagnostics and exports.
/// </summary>
public sealed class LabPage : ContentPage
{
    private static readonly (string Id, string Name)[] Bundled =
    [
        ("nova", "Nova"), ("aurora-flow", "Aurora Flow"), ("plush-mochi", "Mochi"), ("plush-sprig", "Sprig"), ("plush-bean", "Bean"), ("plush-puff", "Puff"), ("plush-home", "Home"), ("pip", "Pip"), ("mpfb", "MPFB Human"), ("aurora-motion", "Aurora Motion"), ("robot-expressive", "Robot Expressive"), ("pebble-bot", "Pebble Bot"),
        ("voice-totem", "Voice Totem"), ("aurora", "Aurora"), ("dotling", "Dotling"),
    ];

    private readonly AvatarView _avatar = new() { HeightRequest = 320, WidthRequest = 320, HorizontalOptions = LayoutOptions.Center };
    private readonly AvatarView _small64 = new() { HeightRequest = 64, WidthRequest = 64 };
    private readonly AvatarView _small128 = new() { HeightRequest = 128, WidthRequest = 128 };
    private readonly Image _poster = new() { HeightRequest = 96, WidthRequest = 96, Aspect = Aspect.AspectFit };
    private readonly Label _status = LabUi.Small(secondary: false);
    private readonly Label _diagnostics = LabUi.Small(mono: true);
    private readonly Label _voiceStatus = LabUi.Small();
    private readonly ChipGroup _avatars = new();
    private readonly ChipGroup _states = new();
    private readonly ChipGroup _gestures = new();
    private readonly ChipGroup _themes = new();
    private readonly ChipGroup _motions = new();
    private readonly ChipGroup _frameRates = new();
    private readonly ChipGroup _sizes = new();
    private readonly ChipGroup _renderers = new();
    private readonly Picker _expression = LabUi.Picker(AvatarVocabulary.Expressions, 1);
    private readonly Slider _intensity = LabUi.Slider(0, 1, 0.65);
    private readonly Slider _duration = LabUi.Slider(0.25, 12, 2.5);
    private readonly Slider _input = LabUi.Slider(0, 1, 0);
    private readonly Slider _output = LabUi.Slider(0, 1, 0);
    private readonly Switch _bands = LabUi.Switch();
    private readonly Picker _viseme = LabUi.Picker([.. AvatarVocabulary.Visemes.Select((v, i) => $"{i} {v}")], 10);
    private readonly Switch _holdViseme = LabUi.Switch();
    private readonly Switch _microphone = LabUi.Switch();
    private readonly Switch _muted = LabUi.Switch();
    private readonly Switch _animation = LabUi.Switch(on: true);
    private readonly Switch _accent = LabUi.Switch();
    private readonly Switch _previews = LabUi.Switch(on: true);
    private readonly Switch _studio = LabUi.Switch(on: true);
    private readonly Slider _spring = LabUi.Slider(0, 2, 1);
    private readonly Entry _seed = new() { Text = "0", Keyboard = Keyboard.Numeric, WidthRequest = 80 };
    private readonly Editor _text = new() { Text = "Hej Jonatan. Jag är en referensavatar för din nya Spine-kontroll.", AutoSize = EditorAutoSizeOption.TextChanges, FontSize = 14 };
    private readonly Label _timeline = LabUi.Small(mono: true);
    private readonly ProgressBar _progress = new();

    private AvatarFixtureFeed? _fixture;
    private readonly AvatarTextSpeechFeed _tts = new();
    private readonly AvatarMicrophoneFeed _mic = new();
    private IDispatcherTimer? _diagnosticsTimer;
    private string _sourceName = "pip";

    public LabPage()
    {
        Title = "Avatar Lab";
        LabUi.Page(this);
        _text.SetAppThemeColor(Editor.TextColorProperty, Color.FromArgb("#16202B"), Color.FromArgb("#E7ECF2"));
        _progress.SetAppThemeColor(ProgressBar.ProgressColorProperty, LabUi.Accent, LabUi.AccentDark);

        _small64.MirrorOf = _avatar;
        _small128.MirrorOf = _avatar;
        _avatar.AvatarLoaded += (_, _) => OnAvatarLoaded();
        _avatar.AvatarFailed += (_, e) => _status.Text = $"Failed: {e.Message}";
        _avatar.FrameRendered += OnFrame;
        _avatar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AvatarView.State))
                _states.Select(AvatarVocabulary.Name(_avatar.State));
        };

        foreach (var (id, name) in Bundled)
            _avatars.Add(id, name, () => LoadBundled(id));
        foreach (var state in Enum.GetValues<AvatarState>())
            _states.Add(AvatarVocabulary.Name(state), AvatarVocabulary.Name(state), () => _avatar.State = state);
        _themes.Add("system", "System", () => SetTheme(AppTheme.Unspecified)).Add("light", "Light", () => SetTheme(AppTheme.Light)).Add("dark", "Dark", () => SetTheme(AppTheme.Dark));
        foreach (var mode in Enum.GetValues<AvatarMotionMode>())
            _motions.Add(mode.ToString(), mode.ToString(), () => _avatar.MotionMode = mode);
        foreach (var fps in new[] { 15, 30, 60 })
            _frameRates.Add(fps.ToString(CultureInfo.InvariantCulture), $"{fps} fps", () => _avatar.MaxFramesPerSecond = fps);
        foreach (var size in new[] { 64, 128, 320, 512 })
            _sizes.Add(size.ToString(CultureInfo.InvariantCulture), $"{size}", () => _avatar.WidthRequest = _avatar.HeightRequest = size);
        _renderers.Add("native", "Native", () => SetRenderer("native")).Add("web", "WebView (three.js)", () => SetRenderer("web"));
        _themes.Select("system");
        _motions.Select(nameof(AvatarMotionMode.System));
        _frameRates.Select("60");
        _sizes.Select("320");
        _renderers.Select("native");
        _states.Select("idle");

        _mirrors.Children.Add(_small64);
        _mirrors.Children.Add(_small128);
        _mirrors.Children.Add(_poster);
        var diagnosticsToggle = LabUi.Chip("Diagnostics", () => _diagnostics.IsVisible = !_diagnostics.IsVisible);
        var stage = new Border
        {
            Padding = new Thickness(12),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    _avatar,
                    _mirrors,
                },
            },
        };
        stage.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#E9EEF3"), Color.FromArgb("#141920"));

        var preview = new VerticalStackLayout
        {
            Spacing = 10,
            Children = { stage, _status, LabUi.Row(diagnosticsToggle), _diagnostics },
        };

        var controls = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(0, 0, 4, 40),
            Children =
            {
                LabUi.Card("Avatar", _avatars.View,
                    LabUi.Row(LabUi.Chip("Open file…", OpenFile), LabUi.Chip("Validation report", ShowReport), LabUi.Chip("Reload", () => _ = _avatar.LoadAsync()))),

                LabUi.Card("Voice",
                    LabUi.Labeled("Listen (mic)", _microphone),
                    _voiceStatus,
                    _text,
                    LabUi.Row(LabUi.Chip("Speak (TTS)", Speak), LabUi.Chip("Cancel", () => _tts.Cancel())),
                    LabUi.Small().With("Timed fixture: WAV with canonical viseme cues"),
                    LabUi.Row(LabUi.Chip("Play", PlayFixture), LabUi.Chip("Pause", () => _fixture?.Pause()), LabUi.Chip("Resume", () => _fixture?.Resume()), LabUi.Chip("Interrupt", InterruptFixture)),
                    _progress,
                    _timeline),

                LabUi.Card("State", _states.View, LabUi.Labeled("Mic-off badge", _muted)),

                LabUi.Card("Expression",
                    _expression,
                    LabUi.Labeled("Intensity", _intensity),
                    LabUi.Labeled("Duration (s)", _duration),
                    LabUi.Row(LabUi.Chip("Show for duration", ShowExpression), LabUi.Chip("Set as base", SetBaseExpression))),

                LabUi.Card("Gestures", _gestures.View),

                LabUi.Card("Manual levels and visemes",
                    LabUi.Labeled("Input", _input),
                    LabUi.Labeled("Output", _output),
                    LabUi.Labeled("Band generator", _bands),
                    _viseme,
                    LabUi.Labeled("Hold viseme", _holdViseme)),

                LabUi.Card("Appearance",
                    LabUi.Labeled("Theme", _themes.View),
                    LabUi.Labeled("Motion", _motions.View),
                    LabUi.Labeled("Ambient", _animation),
                    LabUi.Labeled("Spring motion", _spring),
                    LabUi.Labeled("Accent override", _accent),
                    LabUi.Labeled("Size (DIP)", _sizes.View),
                    LabUi.Labeled("Previews", _previews)),

                LabUi.Card("3D",
                    LabUi.Labeled("Renderer", _renderers.View),
                    LabUi.Labeled("Studio light", _studio),
                    LabUi.Small().With("Native is SceneKit on iOS and Mac; elsewhere the WebView is used. Studio light applies to the WebView.")),

                LabUi.Card("Performance and export",
                    LabUi.Labeled("Max fps", _frameRates.View),
                    LabUi.Row(LabUi.Text(new Label { Text = "Seed", VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 8, 0) }), _seed, LabUi.Chip("Apply", ApplySeed)),
                    LabUi.Row(LabUi.Chip("Sheets (PNG)", ExportSheets), LabUi.Chip("measured-result.json", ExportMeasurements), LabUi.Chip("Unload/reload ×100", Stress))),
            },
        };

        _preview = preview;
        _controls = controls;
        SizeChanged += (_, _) => ArrangeColumns();
        ArrangeColumns();

        _microphone.Toggled += (_, e) => ToggleMicrophone(e.Value);
        _muted.Toggled += (_, e) => _avatar.IsMuted = e.Value;
        _animation.Toggled += (_, e) => _avatar.IsAnimationEnabled = e.Value;
        _spring.ValueChanged += (_, e) => _avatar.SecondaryMotion = (float)e.NewValue;
        _studio.Toggled += (_, e) => _avatar.ThreeDLook = e.Value ? "studio" : "basic";
        _accent.Toggled += (_, e) => _avatar.AccentColor = e.Value ? Color.FromArgb("#E8590C") : null;
        _previews.Toggled += (_, e) => _small64.IsVisible = _small128.IsVisible = _poster.IsVisible = e.Value;
        _viseme.SelectedIndexChanged += (_, _) => ApplyViseme();
        _holdViseme.Toggled += (_, _) => ApplyViseme();

        LoadBundled("pip");
#if DEBUG || LAB_HARNESS
        LabHarness.Start(this);
#endif
    }

    /// <summary>
    /// One avatar on one 3D renderer, speaking with generated bands, measured over <paramref name="seconds"/>:
    /// frame rate, our time and allocation per frame, the renderer's own time, the app process's CPU and footprint.
    /// </summary>
    private async Task<string> BenchAsync(string avatar, string renderer, double seconds)
    {
        _renderers.Select(renderer);
        _avatar.ThreeDRenderer = _small64.ThreeDRenderer = _small128.ThreeDRenderer = renderer;
        _previews.IsToggled = false;
        LoadBundled(avatar);
        for (var i = 0; i < 100 && (_avatar.LoadState is AvatarLoadState.Loading || _avatar.Package?.Manifest.Id != avatar); i++)
            await Task.Delay(50);
        _bands.IsToggled = true;
        _output.Value = 0.6;
        _avatar.State = AvatarState.Speaking;
        await Task.Delay(2500);

        var stats = _avatar.Stats!;
        var firstFrame = stats.FirstFrameTimestamp > 0 ? System.Diagnostics.Stopwatch.GetElapsedTime(_avatar.LoadStartedTimestamp, stats.FirstFrameTimestamp).TotalMilliseconds : double.NaN;
        stats.ResetPeaks();
        GC.Collect();
        var cpu = ProcessStats.CpuSeconds();
        var wall = System.Diagnostics.Stopwatch.GetTimestamp();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var cpuPercent = (ProcessStats.CpuSeconds() - cpu) / System.Diagnostics.Stopwatch.GetElapsedTime(wall).TotalSeconds * 100;
        var footprint = ProcessStats.FootprintBytes() / (1024.0 * 1024);

        _bands.IsToggled = false;
        _output.Value = 0;
        _avatar.State = AvatarState.Idle;
        _previews.IsToggled = true;
        return string.Create(CultureInfo.InvariantCulture,
            $"{avatar} {_avatar.RendererName}: {stats.FramesPerSecond:0.0} fps, ours p50 {stats.Percentile(0.5):0.00} ms p95 {stats.Percentile(0.95):0.00} ms, alloc peak {stats.MaxAllocatedBytes} B, app CPU {cpuPercent:0} %, footprint {footprint:0} MB, first frame {firstFrame:0} ms | {_avatar.RendererDetail}");
    }

    private void SetTheme(AppTheme theme) => Application.Current!.UserAppTheme = theme;

    private void SetRenderer(string renderer)
    {
        _avatar.ThreeDRenderer = renderer;
        _small64.ThreeDRenderer = _small128.ThreeDRenderer = renderer;
        if (_avatar.Representation?.Renderer == "native3d")
            _ = _avatar.LoadAsync();
    }

    private async void ToggleMicrophone(bool on) => await SetMicrophoneAsync(on);

    private async Task SetMicrophoneAsync(bool on)
    {
        if (!on)
        {
            _mic.Stop();
            _voiceStatus.Text = "";
            return;
        }
        if (_mic.IsRunning)
            return;
        _voiceStatus.Text = "Starting the microphone…";
        if (await _mic.StartAsync(_avatar))
            _voiceStatus.Text = "Listening: the avatar follows the microphone's level and bands.";
        else
        {
            _voiceStatus.Text = $"Microphone: {_mic.Error}";
            _microphone.IsToggled = false;
        }
    }

    /// <summary>One harness command (see <see cref="LabHarness"/>); returns PNG bytes for <c>shot</c>, text otherwise.</summary>
    internal async Task<object?> RunCommandAsync(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var arg = parts.Length > 1 ? parts[1] : "";
        float Number(int i) => float.Parse(parts[i], CultureInfo.InvariantCulture);

        switch (parts[0])
        {
            case "load":
                LoadBundled(arg);
                for (var i = 0; i < 100 && (_avatar.LoadState is AvatarLoadState.Loading || _avatar.Package?.Manifest.Id != arg); i++)
                    await Task.Delay(50);
                return $"{_avatar.LoadState} {_avatar.RendererName} {_avatar.LoadMilliseconds:0} ms";
            case "state":
                _avatar.State = Enum.Parse<AvatarState>(arg, ignoreCase: true);
                return null;
            case "muted":
                _muted.IsToggled = arg == "on";
                return null;
            case "expr":
                _expression.SelectedIndex = Array.IndexOf(AvatarVocabulary.Expressions, arg);
                _intensity.Value = Number(2);
                _duration.Value = Number(3);
                ShowExpression();
                return null;
            case "base":
                _expression.SelectedIndex = Array.IndexOf(AvatarVocabulary.Expressions, arg);
                _intensity.Value = Number(2);
                SetBaseExpression();
                return null;
            case "gesture":
                _ = PlayGesture(arg);
                return null;
            case "viseme":
                if (arg == "off")
                    _holdViseme.IsToggled = false;
                else
                {
                    _viseme.SelectedIndex = int.Parse(arg, CultureInfo.InvariantCulture);
                    _holdViseme.IsToggled = true;
                }
                return null;
            case "level":
                (arg == "in" ? _input : _output).Value = Number(2);
                return null;
            case "bands":
                _bands.IsToggled = arg == "on";
                return null;
            case "fixture":
                switch (arg)
                {
                    case "play": PlayFixture(); break;
                    case "pause": _fixture?.Pause(); break;
                    case "resume": _fixture?.Resume(); break;
                    case "interrupt": InterruptFixture(); break;
                }
                return null;
            case "speak":
                _text.Text = line[6..];
                Speak();
                return null;
            case "theme":
                _themes.Select(arg);
                SetTheme(arg switch { "light" => AppTheme.Light, "dark" => AppTheme.Dark, _ => AppTheme.Unspecified });
                return null;
            case "motion":
                var mode = Enum.Parse<AvatarMotionMode>(arg, ignoreCase: true);
                _motions.Select(mode.ToString());
                _avatar.MotionMode = mode;
                return null;
            case "fps":
                _frameRates.Select(arg);
                _avatar.MaxFramesPerSecond = int.Parse(arg, CultureInfo.InvariantCulture);
                return null;
            case "size":
                _sizes.Select(arg);
                _avatar.WidthRequest = _avatar.HeightRequest = int.Parse(arg, CultureInfo.InvariantCulture);
                return null;
            case "skia":
                _avatar.SkiaOnGpu = _small64.SkiaOnGpu = _small128.SkiaOnGpu = arg == "gpu";
                if (_avatar.Representation?.Renderer == "skia")
                    _ = _avatar.LoadAsync();
                return null;
            case "renderer":
                _renderers.Select(arg);
                SetRenderer(arg);
                return null;
            case "mic":
                await SetMicrophoneAsync(arg == "on");
                return $"{_voiceStatus.Text} running={_mic.IsRunning}";
            case "seed":
                _seed.Text = arg;
                ApplySeed();
                return null;
            case "stress":
                return await StressAsync();
            case "follow":
                _followPointer = arg == "on";
                return null;
            case "lookat":
                Look(arg == "off" ? null : new Point(Number(1), Number(2)), hold: null);
                return null;
            case "look":
                _studio.IsToggled = arg == "studio";
                return null;
            case "spring":
                _spring.Value = Number(1);
                return null;
            case "bench":
                return await BenchAsync(arg, parts[2], parts.Length > 3 ? double.Parse(parts[3], CultureInfo.InvariantCulture) : 6);
            case "reset-peaks":
                _avatar.Stats?.ResetPeaks();
                return null;
            case "dump":
                UpdateDiagnostics();
                return $"\n{_status.Text}\n{_diagnostics.Text}\n{_timeline.Text}";
            case "measure":
                return "\n" + MeasuredResult.Create(_avatar, _sourceName);
            case "report":
                return "\n" + (_avatar.Package?.Report.ToJson() ?? (_avatar.LoadError as AvatarLoadException)?.Report.ToJson() ?? _avatar.LoadError?.Message);
#if DEBUG || LAB_HARNESS
            case "shot":
                await Task.Delay(50);
                return LabHarness.Screenshot(this);
            case "shotlive":
                await Task.Delay(50);
                return LabHarness.ScreenshotInWindow((VisualElement)_avatar.Content!);
            case "shotview":
                await Task.Delay(50);
                return LabHarness.Screenshot((VisualElement)_avatar.Content!);
#endif
            default:
                throw new ArgumentException($"unknown command '{parts[0]}'");
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _diagnosticsTimer ??= Dispatcher.CreateTimer();
        _diagnosticsTimer.Interval = TimeSpan.FromMilliseconds(250);
        _diagnosticsTimer.Tick += (_, _) => UpdateDiagnostics();
        _diagnosticsTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _diagnosticsTimer?.Stop();
    }

    private View _preview = null!, _controls = null!;
    private readonly HorizontalStackLayout _mirrors = new() { Spacing = 14, HorizontalOptions = LayoutOptions.Center };
    private bool? _wide;

    // Wide windows get two columns that scroll on their own; a phone keeps the avatar fixed at the top
    // and scrolls only the options below it, with the small mirrors and the diagnostics folded away.
    private void ArrangeColumns()
    {
        var wide = Width <= 0 || Width > 800;
        if (wide == _wide)
            return;
        _wide = wide;

        (_preview.Parent as Layout)?.Remove(_preview);
        (_controls.Parent as Layout)?.Remove(_controls);
        if (_preview.Parent is ScrollView previewScroll) previewScroll.Content = null;
        if (_controls.Parent is ScrollView controlsScroll) controlsScroll.Content = null;

        _mirrors.IsVisible = wide;
        if (wide)
        {
            var grid = new Grid { Padding = new Thickness(16, 12), ColumnSpacing = 20, ColumnDefinitions = [new(GridLength.Star), new(new GridLength(440))] };
            grid.Add(new ScrollView { Content = _preview });
            grid.Add(new ScrollView { Content = _controls }, 1);
            FollowPointer(grid);
            Content = grid;
        }
        else
        {
            _diagnostics.IsVisible = false;
            var grid = new Grid { Padding = new Thickness(14, 10, 14, 0), RowSpacing = 10, RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
            grid.Add(_preview);
            grid.Add(new ScrollView { Content = _controls }, 0, 1);
            FollowPointer(grid);
            Content = grid;
        }
    }

    private CancellationTokenSource? _lookRelease;
    private bool _followPointer = true;

    // The avatar looks where the mouse is (Mac) or where a finger lands (iPhone), and back at the
    // viewer when the pointer leaves or a moment after the finger lifts.
    private void FollowPointer(View root)
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerMoved += (_, e) => { if (_followPointer) Look(e.GetPosition(_avatar), hold: null); };
        pointer.PointerExited += (_, _) => { if (_followPointer) Look(null, hold: null); };
        root.GestureRecognizers.Add(pointer);
#if IOS
        // MAUI's PointerPressed on a page-wide layout took the touches from the buttons beneath it;
        // a plain tap recognizer that never cancels touches only watches them.
        root.HandlerChanged += (_, _) =>
        {
            if (root.Handler?.PlatformView is not UIKit.UIView view)
                return;
            view.AddGestureRecognizer(new UIKit.UITapGestureRecognizer(tap =>
            {
                if (_avatar.Handler?.PlatformView is UIKit.UIView avatar)
                {
                    var at = tap.LocationInView(avatar);
                    Look(new Point(at.X, at.Y), hold: TimeSpan.FromSeconds(2.5));
                }
            })
            {
                CancelsTouchesInView = false,
                DelaysTouchesBegan = false,
                DelaysTouchesEnded = false,
                ShouldRecognizeSimultaneously = (_, _) => true,
            });
        };
#endif
    }

    private void Look(Point? point, TimeSpan? hold)
    {
        _lookRelease?.Cancel();
        _avatar.LookAt(point);
        if (point is null || hold is null)
            return;
        var release = _lookRelease = new CancellationTokenSource();
        Dispatcher.DispatchDelayed(hold.Value, () =>
        {
            if (!release.IsCancellationRequested)
                _avatar.LookAt(null);
        });
    }

    private void LoadBundled(string name)
    {
        _fixture?.Interrupt();
        _avatars.Select(name);
        _sourceName = name;
        _status.Text = $"Loading {name}…";
        _avatar.Source = AvatarSource.FromMauiAsset($"Avatars/{name}.spineavatar");
    }

    private async void OpenFile()
    {
        try
        {
            var type = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.iOS] = ["public.data", "public.zip-archive"],
                [DevicePlatform.MacCatalyst] = ["public.data", "public.zip-archive"],
                [DevicePlatform.Android] = ["application/zip", "application/octet-stream", "*/*"],
                [DevicePlatform.WinUI] = [".spineavatar"],
            });
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a .spineavatar", FileTypes = type });
            if (file is null)
                return;
            _sourceName = file.FileName;
            _status.Text = $"Loading {file.FileName}…";
            _avatar.Source = AvatarSource.FromStream(file.FileName, async _ => await file.OpenReadAsync());
        }
        catch (Exception e)
        {
            await DisplayAlertAsync("Open file", e.Message, "OK");
        }
    }

    private void OnAvatarLoaded()
    {
        var package = _avatar.Package!;
        var manifest = package.Manifest;
        _gestures.Clear();
        foreach (var gesture in _avatar.Scheduler!.Gestures)
            _gestures.Add(gesture, gesture, () => _ = PlayGesture(gesture));

        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        _poster.Source = package.TryGetFile(dark ? manifest.Posters.Dark : manifest.Posters.Light, out var poster)
            ? ImageSource.FromStream(() => new MemoryStream(poster.ToArray()))
            : null;
        ApplySeed();
    }

    private async Task PlayGesture(string name)
    {
        try
        {
            await _avatar.PlayGestureAsync(name);
        }
        catch (Exception e)
        {
            _status.Text = e.Message;
        }
    }

    private void ShowExpression() =>
        _avatar.SetExpression(new AvatarExpressionRequest((AvatarExpression)_expression.SelectedIndex, (float)_intensity.Value, TimeSpan.FromSeconds(_duration.Value)));

    private void SetBaseExpression()
    {
        _avatar.Expression = (AvatarExpression)_expression.SelectedIndex;
        _avatar.ExpressionIntensity = (float)_intensity.Value;
    }

    private void ApplyViseme() => _avatar.Scheduler?.SetManualViseme(_holdViseme.IsToggled ? _viseme.SelectedIndex : null);

    private void ApplySeed()
    {
        if (int.TryParse(_seed.Text, CultureInfo.InvariantCulture, out var seed))
            _avatar.Seed = seed;
    }

    private readonly float[] _generated = new float[AvatarRenderFrame.BandCount];

    private void OnFrame(object? sender, AvatarRenderFrame frame)
    {
        if (_avatar.Scheduler is not { } scheduler)
            return;

        // Manual levels and the band generator; a running feed sets its own output level after this.
        ReadOnlySpan<float> bands = default;
        if (_bands.IsToggled)
        {
            var t = frame.ElapsedSeconds;
            for (var i = 0; i < _generated.Length; i++)
                _generated[i] = (float)Math.Clamp(0.5 + 0.45 * Math.Sin(t * (2 + i * 0.37) + i), 0, 1);
            bands = _generated;
        }
        if (_input.Value > 0 || _bands.IsToggled)
            scheduler.SetInputLevel((float)_input.Value, bands);
        if (_fixture?.IsRunning != true && (_output.Value > 0 || _bands.IsToggled))
            scheduler.SetOutputLevel((float)_output.Value, bands);
    }

    private async void PlayFixture()
    {
        try
        {
            _fixture ??= await AvatarFixtureFeed.LoadAsync();
            _fixture.Start(_avatar);
        }
        catch (Exception e)
        {
            await DisplayAlertAsync("Fixture", e.Message, "OK");
        }
    }

    private void InterruptFixture()
    {
        if (_fixture?.IsRunning == true)
            _fixture.Interrupt();
        else if (_avatar.Scheduler is not null)
            _avatar.Interrupt();
    }

    private async void Speak()
    {
        try
        {
            await _tts.SpeakAsync(_avatar, new AvatarTextRequest(_text.Text ?? ""));
        }
        catch (Exception e)
        {
            _status.Text = $"TTS: {e.Message}";
        }
    }

    private async void Stress() => _status.Text = await StressAsync();

    private async Task<string> StressAsync()
    {
        // Spec §24: views and loads that come and go must not grow native or managed memory.
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var workingSet = Environment.WorkingSet;
        for (var i = 0; i < 100; i++)
        {
            _avatar.Source = null;
            _avatar.Source = AvatarSource.FromMauiAsset($"Avatars/{Bundled[i % Bundled.Length].Id}.spineavatar");
            await Task.Delay(30);
        }
        await _avatar.LoadAsync();
        await Task.Delay(500);
        var after = GC.GetTotalMemory(forceFullCollection: true);
        return $"100 loads: managed heap {before / 1024} → {after / 1024} KiB, working set {workingSet / (1024 * 1024)} → {Environment.WorkingSet / (1024 * 1024)} MiB";
    }

    private async void ShowReport()
    {
        // A failed load shows the report of the archive that failed; otherwise the loaded one's.
        var report = (_avatar.LoadError as AvatarLoadException)?.Report ?? _avatar.Package?.Report;
        if (report is null)
        {
            await DisplayAlertAsync("Validation", _avatar.LoadError?.Message ?? "No avatar loaded.", "OK");
            return;
        }
        var text = string.Join("\n\n", report.Checks.Select(c => $"{c.Status}  {c.Area}/{c.Name}\n{c.Detail}"));
        await Navigation.PushAsync(new ContentPage
        {
            Title = $"Validation: {report.Summary()}",
            Content = new ScrollView { Content = new Label { Text = text, Padding = 16, FontFamily = "Menlo", FontSize = 12 } },
        });
    }

    private async void ExportSheets()
    {
        try
        {
            if (_avatar.Package is not { } package || _avatar.Representation is not { Renderer: "skia" } representation)
            {
                await DisplayAlertAsync("Sheets", "Sheets are rendered by the Skia renderer; this avatar has none.", "OK");
                return;
            }
            var directory = Path.Combine(FileSystem.CacheDirectory, "sheets");
            Directory.CreateDirectory(directory);
            var files = new List<ShareFile>();
            foreach (var kind in Enum.GetValues<AvatarSheetKind>())
            {
                foreach (var dark in new[] { false, true })
                {
                    var path = Path.Combine(directory, $"{package.Manifest.Id}-{kind}-{(dark ? "dark" : "light")}.png".ToLowerInvariant());
                    await File.WriteAllBytesAsync(path, await Task.Run(() => AvatarSheet.RenderPng(package, representation, kind, dark)));
                    files.Add(new ShareFile(path));
                }
            }
            await Share.Default.RequestAsync(new ShareMultipleFilesRequest { Title = "Avatar sheets", Files = files });
        }
        catch (Exception e)
        {
            await DisplayAlertAsync("Sheets", e.Message, "OK");
        }
    }

    private async void ExportMeasurements()
    {
        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, $"measured-result-{_avatar.Package?.Manifest.Id ?? "none"}-{DeviceInfo.Platform}.json".ToLowerInvariant());
            await File.WriteAllTextAsync(path, MeasuredResult.Create(_avatar, _sourceName));
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "measured-result.json", File = new ShareFile(path) });
        }
        catch (Exception e)
        {
            await DisplayAlertAsync("Export", e.Message, "OK");
        }
    }

    private void UpdateDiagnostics()
    {
        var stats = _avatar.Stats;
        var scheduler = _avatar.Scheduler;
        var frame = _avatar.LastFrame;
        var package = _avatar.Package;

        _status.Text = package is null
            ? $"{_avatar.LoadState} {_avatar.LoadError?.Message}"
            : $"{package.Manifest.DisplayName} {package.Manifest.AssetVersion} · {package.Manifest.Profile} · {_avatar.RendererName} · {_avatar.LoadState} · report {package.Report.Summary()}"
                + (_avatar.LoadState == AvatarLoadState.Failed ? $"\nLast load failed: {_avatar.LoadError?.GetType().Name}: {_avatar.LoadError?.Message}" : "");

        if (stats is null || scheduler is null || frame is null)
            return;

        var d = scheduler.Diagnostics;
        var firstFrame = stats.FirstFrameTimestamp > 0 && _avatar.LoadStartedTimestamp > 0
            ? $"{System.Diagnostics.Stopwatch.GetElapsedTime(_avatar.LoadStartedTimestamp, stats.FirstFrameTimestamp).TotalMilliseconds:0} ms"
            : "–";
        _diagnostics.Text =
            $"state {scheduler.State}  quality {frame.LipSyncQuality}  gen {frame.Generation}\n" +
            $"fps {stats.FramesPerSecond:0.0} (max {_avatar.MaxFramesPerSecond})  render p50 {stats.Percentile(0.5):0.00} ms  p95 {stats.Percentile(0.95):0.00} ms\n" +
            $"alloc/frame {stats.LastAllocatedBytes} B (peak {stats.MaxAllocatedBytes} B)  load {_avatar.LoadMilliseconds:0} ms  first frame {firstFrame}\n" +
            $"cues queued {d.QueuedCues}  accepted {d.AcceptedCues}  stale {d.StaleCues}  rejected {d.RejectedCues}  underruns {d.Underruns}  resets {d.Resets}\n" +
            $"speech pos {d.SpeechPositionSeconds:0.000}s  speaking {frame.SpeakingWeight:0.00}  in {frame.InputLevel:0.00}  out {frame.OutputLevel:0.00}  blink {frame.Blink:0.00}\n" +
            $"clock {AvatarPcmPlayer.ClockDescription}";

        if (_fixture is { IsRunning: true } fixture)
        {
            var snapshot = fixture.Snapshot;
            _progress.Progress = snapshot.Position / fixture.Duration;
            _timeline.Text = $"{snapshot.Position.TotalSeconds:0.000} / {fixture.Duration.TotalSeconds:0.000} s  gen {snapshot.Generation}  playing {snapshot.IsPlaying}  latency {snapshot.EstimatedOutputLatency.TotalMilliseconds:0} ms  presentation {snapshot.PositionIsPresentationTime}";
        }
    }
}

internal static class LabUiExtensions
{
    public static Label With(this Label label, string text)
    {
        label.Text = text;
        return label;
    }
}
