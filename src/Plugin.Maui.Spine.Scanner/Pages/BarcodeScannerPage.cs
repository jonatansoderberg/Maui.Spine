using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.Barcodes;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Scanner;

/// <summary>
/// A sheet that scans until it reads one code and returns it; closing returns no value. The camera fills the sheet
/// under a transparent header with a close button and, when the camera has one, a torch. By default it opens at half
/// height, shows breathing corners where to aim, and on a hit lets the marked code burst towards the user before it
/// closes; <see cref="BarcodeScanOptions"/> changes each of these.
/// </summary>
/// <example>
/// <code><![CDATA[
/// var scan = await navigation.NavigateToWithResultAsync<BarcodeScannerPage, BarcodeScanOptions, BarcodeScanResult>(
///     new BarcodeScanOptions { Formats = BarcodeFormat.QrCode, LightGrid = new LightGridOptions(12, 12) });
/// if (scan is { IsSuccess: true, Value: { } code }) Pair(code.Value);
/// ]]></code>
/// </example>
[NavigableSheet(
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen],
    HeaderBar = HeaderBarMode.Overlay,
    HeaderBarBackground = HeaderBarBackground.Transparent,
    HeaderBarForeground = "#FFFFFF",
    HeaderBarGlass = HeaderBarGlass.Clear,
    SafeAreaEdges = SafeAreaEdges.None)]
public sealed class BarcodeScannerPage : SpinePage<BarcodeScannerPageViewModel>,
    INavigableWithParameter<BarcodeScanOptions>, INavigableWithResult<BarcodeScanResult>
{
    private const string PulseAnimation = "SpineScannerReticlePulse";

    private readonly BarcodeScannerView _scanner = new();
    private readonly ScannerOverlay _overlay = new();
    private readonly Grid _stage;
    private readonly Label _hint = new()
    {
        TextColor = Colors.White,
        FontSize = 17,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap,
    };
    private readonly Border _promptBox;
    private readonly Label _diagnostics = new()
    {
        TextColor = Color.FromRgba(255, 255, 255, 0.8),
        FontSize = 12,
        HorizontalTextAlignment = TextAlignment.Center,
        IsVisible = false,
    };
    private BarcodeScannerPageViewModel? _viewModel;
    private bool _detected;

    public BarcodeScannerPage()
    {
        // Wired in code, not bound: the page and its view model are one unit, and a binding that does not
        // resolve fails silently, which left the sheet reading codes without ever returning one
        _scanner.Detected += OnDetected;
        _scanner.ProblemChanged += (_, e) => { if (_viewModel is { } vm) vm.Message = e.Message; };
        _scanner.PropertyChanged += (_, e) =>
        {
            if (_viewModel is not { } vm) return;
            if (e.PropertyName == nameof(BarcodeScannerView.IsTorchOn)) vm.IsTorchOn = _scanner.IsTorchOn;
            else if (e.PropertyName == nameof(BarcodeScannerView.Diagnostics)) _diagnostics.Text = _scanner.Diagnostics;
        };

        // A pill floating between the aim corners and the bottom edge; placed once the sizes are known
        _promptBox = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            BackgroundColor = Color.FromRgba(0, 0, 0, 0.55),
            Padding = new Thickness(16, 10),
            Margin = new Thickness(16, 0, 16, 36),
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout { Spacing = 4, Children = { _hint, _diagnostics } },
        };
        _promptBox.SizeChanged += (_, _) => PlacePrompt();
        _overlay.SizeChanged += (_, _) => { PlacePrompt(); UpdateScanArea(); };

        // The camera and the marking zoom together after a hit; the prompt stays put
        _stage = new Grid { Children = { _scanner, _overlay } };
        Content = new Grid
        {
            BackgroundColor = Colors.Black,
            IsClippedToBounds = true,
            Children = { _stage, _promptBox },
        };

        Attach(BindingContext as BarcodeScannerPageViewModel);
    }

    // Not Loaded and Unloaded: a sheet's content does not always get them, which left the corners still
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null)
            this.AbortAnimation(PulseAnimation);
        else
            StartPulse();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        Attach(BindingContext as BarcodeScannerPageViewModel);
    }

    private void Attach(BarcodeScannerPageViewModel? viewModel)
    {
        // The base constructor sets the view model before this class's constructor body has run
        if (_stage is null || viewModel == _viewModel) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = viewModel;
        if (viewModel is null) return;
        viewModel.PropertyChanged += OnViewModelChanged;
        foreach (var name in new[] { nameof(viewModel.Formats), nameof(viewModel.LightGrid), nameof(viewModel.IsScanning),
                     nameof(viewModel.IsTorchOn), nameof(viewModel.Hint), nameof(viewModel.ShowDiagnostics), nameof(viewModel.Options) })
            OnViewModelChanged(viewModel, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is not { } vm) return;
        switch (e.PropertyName)
        {
            case nameof(vm.Formats): _scanner.Formats = vm.Formats; UpdateReticle(); break;
            case nameof(vm.LightGrid): _scanner.LightGrid = vm.LightGrid; UpdateReticle(); break;
            case nameof(vm.IsScanning): _scanner.IsScanning = vm.IsScanning; break;
            case nameof(vm.IsTorchOn): _scanner.IsTorchOn = vm.IsTorchOn; break;
            case nameof(vm.Hint): _hint.Text = vm.Hint; UpdatePrompt(); break;
            case nameof(vm.ShowDiagnostics): _diagnostics.IsVisible = vm.ShowDiagnostics; UpdatePrompt(); break;
            case nameof(vm.Options): UpdateReticle(); UpdatePrompt(); break;
        }
    }

    // Halfway between the aim corners and the bottom edge; without corners, above the edge. Moved by translation, not
    // margin: a new margin laid the box out again at a height a rounding error off, which placed it anew, round and
    // round, freezing the app while the sheet was dragged; near the bottom the margin also squeezed it to nothing.
    private void PlacePrompt()
    {
        double width = _overlay.Width, height = _overlay.Height, box = _promptBox.Height;
        if (width <= 0 || height <= 0 || box <= 0) return;
        if (_overlay.Reticle == ReticleShape.None)
        {
            _promptBox.VerticalOptions = LayoutOptions.End;
            _promptBox.Margin = new Thickness(16, 0, 16, 36);
            _promptBox.TranslationY = 0;
            return;
        }
        var frame = ScannerOverlay.ReticleFor(new RectF(0, 0, (float)width, (float)height), _overlay.Reticle);
        _promptBox.VerticalOptions = LayoutOptions.Start;
        _promptBox.Margin = new Thickness(16, 0);
        _promptBox.TranslationY = Math.Round(Math.Max(frame.Bottom + 8, (frame.Bottom + height) / 2 - box / 2));
    }

    private void UpdateReticle()
    {
        if (_viewModel is not { } vm) return;
        bool twoDimensional = (vm.Formats & BarcodeFormat.TwoDimensional) != 0 || vm.LightGrid is not null;
        _overlay.Reticle = !vm.Options.ShowReticle ? ReticleShape.None
            : !twoDimensional && (vm.Formats & BarcodeFormat.OneDimensional) != 0 ? ReticleShape.Wide
            : ReticleShape.Square;
        _overlay.Invalidate();
        PlacePrompt();
        UpdateScanArea();
    }

    // A code counts when its centre is inside the aim corners, with room around them: a code held a little off still
    // counts, one elsewhere in the picture does not. Without corners, the whole camera counts.
    private void UpdateScanArea()
    {
        double width = _overlay.Width, height = _overlay.Height;
        if (_overlay.Reticle == ReticleShape.None || width <= 0 || height <= 0)
        {
            _scanner.ScanArea = null;
            return;
        }
        var frame = ScannerOverlay.ReticleFor(new RectF(0, 0, (float)width, (float)height), _overlay.Reticle);
        float margin = Math.Min(frame.Width, frame.Height) * 0.2f;
        _scanner.ScanArea = frame.Inflate(margin, margin);
    }

    // The text box shows when asked for, and whenever the scanner has a problem to say
    private void UpdatePrompt()
    {
        if (_viewModel is not { } vm) return;
        _hint.IsVisible = vm.Options.ShowPrompt || vm.Message is not null;
        _promptBox.IsVisible = _hint.IsVisible || vm.ShowDiagnostics;
    }

    private void StartPulse()
    {
        if (this.AnimationIsRunning(PulseAnimation))
            return;
        if (ReduceMotion.IsEnabled)
        {
            _overlay.Pulse = 0;
            _overlay.Invalidate();
            return;
        }
        // Out and back in one cycle, eased at both ends like a breath, so each repeat starts where the last one ended
        new Animation(v => { _overlay.Pulse = (1 - Math.Cos(2 * Math.PI * v)) / 2; _overlay.Invalidate(); }, 0, 1, Easing.Linear)
            .Commit(this, PulseAnimation, length: 1600, repeat: () => !_detected, finished: (_, _) => { });
    }

    private async void OnDetected(object? sender, BarcodeDetectedEventArgs e)
    {
        if (_viewModel is not { } vm || _detected || !vm.BeginReturn()) return;
        _detected = true;
        this.AbortAnimation(PulseAnimation);

        if (vm.Options.ShowDetection)
        {
            try { await ShowDetectionAsync(e.Result); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Spine.Scanner] detection animation: {ex}"); }
        }
        await vm.ReturnAsync(e.Result);
    }

    /// <summary>
    /// The code that was read bursts towards the user the moment it is read: the marking appears on the frozen frame,
    /// straightens to a flat code square to the screen in the middle of the aim corners, grows four and a half times
    /// from there and fades, while the frame behind it dims to half. The sheet closes once it has finished.
    /// </summary>
    private async Task ShowDetectionAsync(BarcodeScanResult result)
    {
        _overlay.ShowHit(result);
        await AnimateAsync("SpineScannerHitIn", v => _overlay.HitProgress = v, 0, 1, 120);

        if (result.Corners is not { Count: 4 } corners || ReduceMotion.IsEnabled || _overlay.Width <= 0 || _overlay.Height <= 0)
        {
            await Task.WhenAll(
                AnimateAsync("SpineScannerHitOut", v => _overlay.HitProgress = v, 1, 0, 250),
                _scanner.FadeToAsync(0.5, 250));
            return;
        }

        // The code moves into the middle of the aim corners as it straightens, and the burst grows from there
        PointF centre = _overlay.Reticle == ReticleShape.None
            ? new((float)corners.Average(p => p.X), (float)corners.Average(p => p.Y))
            : ScannerOverlay.ReticleFor(new RectF(0, 0, (float)_overlay.Width, (float)_overlay.Height), _overlay.Reticle).Center;
        _overlay.StraightenTo = centre;
        _overlay.AnchorX = Math.Clamp(centre.X / _overlay.Width, 0, 1);
        _overlay.AnchorY = Math.Clamp(centre.Y / _overlay.Height, 0, 1);
        // It straightens early and bursts late: the code is flat and in place before it flies past
        await Task.WhenAll(
            AnimateAsync("SpineScannerStraighten", v => _overlay.Straighten = v, 0, 1, 450),
            _overlay.ScaleToAsync(4.5, 450, Easing.CubicIn),
            _overlay.FadeToAsync(0, 450, Easing.CubicIn),
            _scanner.FadeToAsync(0.5, 450, Easing.CubicOut));
    }

    private Task AnimateAsync(string name, Action<double> step, double from, double to, uint length)
    {
        var done = new TaskCompletionSource();
        new Animation(v => { step(v); _overlay.Invalidate(); }, from, to, Easing.CubicOut)
            .Commit(this, name, length: length, finished: (_, _) => done.TrySetResult());
        return done.Task;
    }

}

public sealed partial class BarcodeScannerPageViewModel : ViewModelBase, IReceivesNavigationParameter<BarcodeScanOptions>, ISheetDetentsProvider
{
    private readonly INavigationService _navigation;
    private readonly PageAction _torch;
    private string _prompt = ScannerStrings.Get("Prompt");
    private bool _returned;

    public BarcodeScannerPageViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        Title = ScannerStrings.Get("Title");
        Hint = _prompt;
        // Spine's own close button trails while the header has no other action, and moves to the leading
        // slot when the torch takes the trailing one
        _torch = new PageAction(null, ToggleTorchCommand)
        {
            Svg = "torch.svg", Description = ScannerStrings.Get("Torch"), Haptic = Haptic.Light, IsVisible = false,
        };
        PageActions.Add(_torch);
        UpdateActions();
    }

    [ObservableProperty]
    public partial BarcodeScanOptions Options { get; private set; } = new();

    [ObservableProperty]
    public partial BarcodeFormat Formats { get; set; } = BarcodeFormat.All;

    [ObservableProperty]
    public partial LightGridOptions? LightGrid { get; set; }

    [ObservableProperty]
    public partial bool IsScanning { get; set; } = true;

    [ObservableProperty]
    public partial bool IsTorchOn { get; set; }

    /// <summary>Known when the sheet is created, not when the camera starts, so the header never changes under the user.</summary>
    [ObservableProperty]
    public partial bool IsTorchAvailable { get; set; } = TorchSupport.IsAvailable;

    [ObservableProperty]
    public partial bool ShowDiagnostics { get; set; }

    /// <summary>The prompt, or the problem that stops scanning.</summary>
    [ObservableProperty]
    public partial string Hint { get; set; } = "";

    /// <summary>A problem message from the scanner, or <see langword="null"/> once it scans again.</summary>
    public string? Message
    {
        get;
        set
        {
            field = value;
            Hint = value ?? _prompt;
            OnPropertyChanged(nameof(Hint));
            // Disabled rather than hidden: the header is settled before the sheet shows and never moves under the user
            _torch.IsEnabled = value is null;
        }
    }

    IReadOnlyList<string>? ISheetDetentsProvider.AllowedDetents => Options.Detents;

    string? ISheetDetentsProvider.InitialDetent => Options.InitialDetent;

    public Task OnNavigationParameterAsync(BarcodeScanOptions param)
    {
        Options = param;
        Formats = param.Formats;
        LightGrid = param.LightGrid;
        ShowDiagnostics = param.ShowDiagnostics;
        UpdateActions();
        if (param.Title is { } title) Title = title;
        if (param.Prompt is { } prompt) Hint = _prompt = prompt;
        return Task.CompletedTask;
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        Stop();
        return base.OnDisappearingAsync(navigationDirection);
    }

    public override Task OnDismissedAsync()
    {
        Stop();
        return base.OnDismissedAsync();
    }

    /// <summary>Claims the one result this sheet returns: stops the camera, which leaves the frame that was read on screen.</summary>
    internal bool BeginReturn()
    {
        if (_returned) return false;
        _returned = true;
        Stop();
        Haptics.Play(Haptic.Success);
        if (Options.PlaySound) ScanSound.Play();
        return true;
    }

    internal Task ReturnAsync(BarcodeScanResult result) => _navigation.ReturnAsync(result);

    private void Stop()
    {
        IsScanning = false;
        IsTorchOn = false;
    }

    partial void OnIsTorchAvailableChanged(bool value) => UpdateActions();

    private void UpdateActions() => _torch.IsVisible = Options.ShowTorch && IsTorchAvailable;

    partial void OnIsTorchOnChanged(bool value) => _torch.IsSelected = value;

    [RelayCommand]
    private void ToggleTorch() => IsTorchOn = !IsTorchOn;
}
