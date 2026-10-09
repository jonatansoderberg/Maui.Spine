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
    private static readonly string[] Bundled = ["dotling", "voice-totem", "pebble-bot"];

    private readonly AvatarView _avatar = new() { HeightRequest = 320, WidthRequest = 320, HorizontalOptions = LayoutOptions.Center };
    private readonly AvatarView _small64 = new() { HeightRequest = 64, WidthRequest = 64 };
    private readonly AvatarView _small128 = new() { HeightRequest = 128, WidthRequest = 128 };
    private readonly Image _poster = new() { HeightRequest = 128, WidthRequest = 128, Aspect = Aspect.AspectFit };
    private readonly Label _status = Small();
    private readonly Label _diagnostics = Small(mono: true);
    private readonly FlexLayout _states = Wrap();
    private readonly FlexLayout _gestures = Wrap();
    private readonly Picker _expression = new() { ItemsSource = AvatarVocabulary.Expressions, SelectedIndex = 1 };
    private readonly Slider _intensity = new(0, 1, 0.65);
    private readonly Slider _duration = new(0.25, 12, 2.5);
    private readonly Slider _input = new(0, 1, 0);
    private readonly Slider _output = new(0, 1, 0);
    private readonly Switch _bands = new();
    private readonly Picker _viseme = new() { ItemsSource = AvatarVocabulary.Visemes.Select((v, i) => $"{i} {v}").ToList(), SelectedIndex = 10 };
    private readonly Switch _holdViseme = new();
    private readonly Switch _muted = new();
    private readonly Switch _animation = new() { IsToggled = true };
    private readonly Switch _accent = new();
    private readonly Switch _previews = new() { IsToggled = true };
    private readonly Switch _studio = new() { IsToggled = true };
    private readonly Slider _spring = new(0, 2, 1);
    private readonly Picker _motion = new() { ItemsSource = Enum.GetNames<AvatarMotionMode>(), SelectedIndex = 0 };
    private readonly Picker _theme = new() { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedIndex = 0 };
    private readonly Picker _fps = new() { ItemsSource = new[] { "15", "30", "60" }, SelectedIndex = 2 };
    private readonly Picker _size = new() { ItemsSource = new[] { "64", "128", "320", "512" }, SelectedIndex = 2 };
    private readonly Entry _seed = new() { Text = "0", Keyboard = Keyboard.Numeric, WidthRequest = 80 };
    private readonly Editor _text = new() { Text = "Hej Jonatan. Jag är en referensavatar för din nya Spine-kontroll.", AutoSize = EditorAutoSizeOption.TextChanges };
    private readonly Label _timeline = Small(mono: true);
    private readonly ProgressBar _progress = new();

    private AvatarFixtureFeed? _fixture;
    private readonly AvatarTextSpeechFeed _tts = new();
    private IDispatcherTimer? _diagnosticsTimer;
    private string _sourceName = "dotling";

    public LabPage()
    {
        Title = "Avatar Lab";

        _small64.MirrorOf = _avatar;
        _small128.MirrorOf = _avatar;
        _avatar.AvatarLoaded += (_, _) => OnAvatarLoaded();
        _avatar.AvatarFailed += (_, e) => _status.Text = $"Failed: {e.Message}";
        _avatar.FrameRendered += OnFrame;

        var preview = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Border { Content = _avatar, StrokeThickness = 0, Padding = 0, HorizontalOptions = LayoutOptions.Center },
                new HorizontalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.Center, Children = { _small64, _small128, _poster } },
                _status,
                _diagnostics,
            },
        };

        var controls = new VerticalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(0, 0, 0, 40),
            Children =
            {
                Header("Avatar"),
                Row(Button("Dotling", () => LoadBundled("dotling")), Button("Voice Totem", () => LoadBundled("voice-totem")), Button("Pebble Bot", () => LoadBundled("pebble-bot")), Button("Open file…", OpenFile)),
                Row(Button("Validation report", ShowReport), Button("Reload", () => _ = _avatar.LoadAsync()), Button("Unload/reload ×100", Stress)),

                Header("State"),
                _states,
                Labeled("Microphone off", _muted),

                Header("Expression"),
                _expression,
                Labeled("Intensity", _intensity),
                Labeled("Duration (s)", _duration),
                Row(Button("Show for duration", ShowExpression), Button("Set as base", SetBaseExpression)),

                Header("Gestures"),
                _gestures,

                Header("Levels"),
                Labeled("Input", _input),
                Labeled("Output", _output),
                Labeled("Band generator", _bands),

                Header("Visemes"),
                _viseme,
                Labeled("Hold viseme", _holdViseme),

                Header("Timed fixture (WAV + cues)"),
                Row(Button("Play", PlayFixture), Button("Pause", () => _fixture?.Pause()), Button("Resume", () => _fixture?.Resume()), Button("Interrupt", InterruptFixture)),
                _progress,
                _timeline,

                Header("Platform TTS (EstimatedText)"),
                _text,
                Row(Button("Speak", Speak), Button("Cancel", () => _tts.Cancel())),

                Header("Appearance"),
                Labeled("Theme", _theme),
                Labeled("Motion", _motion),
                Labeled("Ambient animation", _animation),
                Labeled("Spring motion", _spring),
                Labeled("Studio 3D light", _studio),
                Labeled("Accent override", _accent),
                Labeled("Max fps", _fps),
                Labeled("Main size (DIP)", _size),
                Labeled("64/128 previews", _previews),
                Row(new Label { Text = "Seed", VerticalOptions = LayoutOptions.Center }, _seed, Button("Apply", ApplySeed)),

                Header("Export"),
                Row(Button("Sheets (PNG)", ExportSheets), Button("measured-result.json", ExportMeasurements)),
            },
        };

        var scroll = new ScrollView { Content = controls };
        var grid = new Grid { Padding = new Thickness(16, 8), ColumnSpacing = 24, RowSpacing = 8 };
        grid.Add(preview);
        grid.Add(scroll);
        grid.SizeChanged += (_, _) => Layout(grid, preview, scroll);
        Content = grid;

        foreach (var state in Enum.GetValues<AvatarState>())
            _states.Children.Add(Button(AvatarVocabulary.Name(state), () => _avatar.State = state));

        _muted.Toggled += (_, e) => _avatar.IsMuted = e.Value;
        _animation.Toggled += (_, e) => _avatar.IsAnimationEnabled = e.Value;
        _spring.ValueChanged += (_, e) => _avatar.SecondaryMotion = (float)e.NewValue;
        _studio.Toggled += (_, e) => _avatar.ThreeDLook = e.Value ? "studio" : "basic";
        _accent.Toggled += (_, e) => _avatar.AccentColor = e.Value ? Color.FromArgb("#E8590C") : null;
        _previews.Toggled += (_, e) => _small64.IsVisible = _small128.IsVisible = e.Value;
        _motion.SelectedIndexChanged += (_, _) => _avatar.MotionMode = (AvatarMotionMode)_motion.SelectedIndex;
        _theme.SelectedIndexChanged += (_, _) => Application.Current!.UserAppTheme = (AppTheme)_theme.SelectedIndex;
        _fps.SelectedIndexChanged += (_, _) => _avatar.MaxFramesPerSecond = int.Parse((string)_fps.SelectedItem, CultureInfo.InvariantCulture);
        _size.SelectedIndexChanged += (_, _) => _avatar.WidthRequest = _avatar.HeightRequest = int.Parse((string)_size.SelectedItem, CultureInfo.InvariantCulture);
        _viseme.SelectedIndexChanged += (_, _) => ApplyViseme();
        _holdViseme.Toggled += (_, _) => ApplyViseme();

        LoadBundled("dotling");
#if DEBUG || LAB_HARNESS
        LabHarness.Start(this);
#endif
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
                _theme.SelectedIndex = arg switch { "light" => 1, "dark" => 2, _ => 0 };
                return null;
            case "motion":
                _motion.SelectedIndex = (int)Enum.Parse<AvatarMotionMode>(arg, ignoreCase: true);
                return null;
            case "fps":
                _fps.SelectedItem = arg;
                return null;
            case "size":
                _size.SelectedItem = arg;
                return null;
            case "seed":
                _seed.Text = arg;
                ApplySeed();
                return null;
            case "stress":
                return await StressAsync();
            case "look":
                _studio.IsToggled = arg == "studio";
                return null;
            case "spring":
                _spring.Value = Number(1);
                return null;
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

    private static void Layout(Grid grid, View preview, View controls)
    {
        var wide = grid.Width > 800;
        grid.ColumnDefinitions = wide ? [new(GridLength.Star), new(new GridLength(420))] : [new(GridLength.Star)];
        grid.RowDefinitions = wide ? [new(GridLength.Star)] : [new(GridLength.Auto), new(GridLength.Star)];
        Grid.SetColumn(controls, wide ? 1 : 0);
        Grid.SetRow(controls, wide ? 0 : 1);
    }

    private void LoadBundled(string name)
    {
        _fixture?.Interrupt();
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
        _gestures.Children.Clear();
        foreach (var gesture in _avatar.Scheduler!.Gestures)
            _gestures.Children.Add(Button(gesture, () => _ = PlayGesture(gesture)));

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
            _avatar.Source = AvatarSource.FromMauiAsset($"Avatars/{Bundled[i % Bundled.Length]}.spineavatar");
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
            : $"{package.Manifest.DisplayName} {package.Manifest.AssetVersion} · {package.Manifest.Profile} · {_avatar.RendererName} · {_avatar.LoadState} · report {package.Report.Summary()}";

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

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, Padding = new Thickness(12, 4), Margin = new Thickness(0, 0, 6, 6), FontSize = 13 };
        button.Clicked += (_, _) => action();
        return button;
    }

    private static Label Header(string text) => new() { Text = text, FontAttributes = FontAttributes.Bold, FontSize = 15, Margin = new Thickness(0, 12, 0, 2) };

    private static Label Small(bool mono = false) => new() { FontSize = 12, FontFamily = mono ? "Menlo" : null, LineBreakMode = LineBreakMode.WordWrap };

    private static FlexLayout Wrap() => new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };

    private static FlexLayout Row(params View[] views)
    {
        var row = Wrap();
        foreach (var view in views)
            row.Children.Add(view);
        return row;
    }

    private static Grid Labeled(string label, View view)
    {
        var grid = new Grid { ColumnDefinitions = [new(new GridLength(130)), new(GridLength.Star)], ColumnSpacing = 8 };
        grid.Add(new Label { Text = label, VerticalOptions = LayoutOptions.Center, FontSize = 13 });
        grid.Add(view, 1);
        return grid;
    }
}
