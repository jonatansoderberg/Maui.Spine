using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Highlight;

public sealed partial class SyncRow(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsSyncing { get; set; }
}

public partial class HighlightPageViewModel(IHighlights _highlights, INavigationService _navigation) : SampleViewModel
{
    /// <summary>The key of the one-off hint; "Show tips again" forgets it.</summary>
    public const string HintKey = "showcase.highlight.add";

    const string TourKey = "showcase.highlight.tour";
    const string SpotlightKey = "showcase.highlight.spotlight";

    [ObservableProperty]
    public partial bool IsSummarising { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "Fourteen events this week, three of them clash.";

    [ObservableProperty]
    public partial bool IsPlaying { get; set; } = true;

    [ObservableProperty]
    public partial HighlightRepeat Repeat { get; set; }

    [ObservableProperty]
    public partial double Intensity { get; set; } = 1;

    [ObservableProperty]
    public partial double Outset { get; set; } = 12;

    [ObservableProperty]
    public partial bool UseAccent { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Color>? EdgeColors { get; set; }

    [ObservableProperty]
    public partial string HintState { get; set; } = "";

    [ObservableProperty]
    public partial string Log { get; set; } = "Nothing yet";

    [ObservableProperty]
    public partial string TourLog { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSpotlightOn { get; set; }

    [ObservableProperty]
    public partial string EdgeState { get; set; } = "Off";

    IEdgeGlow? _edge;

    public IReadOnlyList<SyncRow> Rows { get; } = [.. Enumerable.Range(1, 30).Select(i => new SyncRow($"Album {i}") { IsSyncing = i % 4 == 1 })];

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        UpdateHintState();
        Plugin.Maui.Spine.Controls.Highlight.Ended -= OnEnded;
        Plugin.Maui.Spine.Controls.Highlight.Ended += OnEnded;
        return Task.CompletedTask;
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        Plugin.Maui.Spine.Controls.Highlight.Ended -= OnEnded;
        if (navigationDirection == NavigationDirection.Back)
            StopEdge();
        return Task.CompletedTask;
    }

    /// <summary>The glow round the window with Highlight's own Edge animation: a model is working.</summary>
    [RelayCommand]
    private void ModelWorking()
    {
        Edge().Intensity = null;
        EdgeState = "A model is working";
    }

    /// <summary>The glow driven by a level, as a voice session's output level would drive it.</summary>
    [RelayCommand]
    private void Speaking()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Edge().Intensity = () => SimulatedSpeech(clock.Elapsed.TotalSeconds);
        EdgeState = "Speaking (a simulated voice level)";
    }

    [RelayCommand]
    private void StopEdge()
    {
        _edge?.Dispose();
        _edge = null;
        EdgeState = "Off";
    }

    [RelayCommand]
    private Task OpenSheet() => _navigation.NavigateToAsync<SimpleBottomSheetPage>();

    IEdgeGlow Edge() => _edge ??= _highlights.AttachEdge(Application.Current!.Windows[0]);

    /// <summary>Phrases of syllables with pauses between them, as on the concept page.</summary>
    static float SimulatedSpeech(double t)
    {
        static double Noise(double x) => 0.5 + 0.25 * Math.Sin(x * 2.1) + 0.25 * Math.Sin(x * 5.3 + 1.7);
        var phrase = Noise(t * 0.55) > 0.32 ? 1 : 0.05;
        var syllable = Math.Pow(Math.Max(0, Math.Sin(t * 2 * Math.PI * (3.6 + Noise(t) * 1.6))), 0.7);
        return (float)Math.Min(1, phrase * (0.25 + 0.75 * syllable) * (0.55 + 0.6 * Noise(t * 3)));
    }

    partial void OnUseAccentChanged(bool value) =>
        EdgeColors = value ? [Application.Current?.Resources.TryGetValue("Primary", out var primary) == true && primary is Color color ? color : Colors.CornflowerBlue] : null;

    [RelayCommand]
    private async Task Summarise()
    {
        IsSummarising = true;
        Summary = "Summarising…";
        await Task.Delay(TimeSpan.FromSeconds(4));
        Summary = "Fourteen events this week. Tuesday's stand-up clashes with the dentist.";
        IsSummarising = false;
    }

    [RelayCommand]
    private void PlayAgain()
    {
        IsPlaying = false;
        IsPlaying = true;
    }

    [RelayCommand]
    private void ShowTipsAgain()
    {
        _highlights.Reset();
        UpdateHintState();
    }

    [RelayCommand]
    private async Task PlayFromCode(View view)
    {
        Log = "Sheen, twice…";
        await _highlights.PlayAsync(view, HighlightKind.Sheen, HighlightRepeat.Times(2));
        Log = "PlayAsync completed";
    }

    /// <summary>A tour across the page: a card far up, a button, the header's palette and a hidden view.</summary>
    [RelayCommand]
    private async Task TakeTour(View demo)
    {
        // The page's named views, found from the example that runs the tour.
        View[] views = [demo.FindByName<View>("SummaryCard"), demo.FindByName<View>("TourAddButton"), demo.FindByName<View>("TourHidden")];

        TourLog = "";
        _highlights.Reset(TourKey);
        var palette = PageActions.First(a => a.Svg == "palette.svg");

        var result = await _highlights.TourAsync(TourKey,
            new TourStep(views[0], "A card that a model works on. The tour scrolled it into view.") { Title = "Summaries" },
            (views[1], "Add an event from here."),
            new TourStep(palette, "Theme and accent live up here.") { Title = "Your look" },
            (views[2], "This view is hidden, so the tour leaves it out."));

        TourLog = result.Missing.Count == 0
            ? $"{result.Outcome}"
            : $"{result.Outcome}; left out: {string.Join(", ", result.Missing.Select(m => $"step {m.Step + 1} ({m.Reason})"))}";
    }

    [RelayCommand]
    private void ShowSpotlight()
    {
        _highlights.Reset(SpotlightKey);
        IsSpotlightOn = true;
    }

    [RelayCommand]
    private void ToggleSync(SyncRow row) => row.IsSyncing = !row.IsSyncing;

    [RelayCommand]
    private Task ShowKindOptions() => ShowOptionsAsync("Highlight",
        new ChoiceGroup("Repeat",
        [
            new Choice("Default", "The kind's own: Edge, Trace and Breathe until turned off, Pulse and Sheen once.", () => Repeat = default),
            new Choice("Once", "One cycle, then it fades.", () => Repeat = HighlightRepeat.Once),
            new Choice("3 times", "Three cycles.", () => Repeat = HighlightRepeat.Times(3)),
            new Choice("Forever", "Until IsActive turns false.", () => Repeat = HighlightRepeat.Forever),
            new Choice("Until tapped", "Tap a card to end its highlight.", () => Repeat = HighlightRepeat.UntilTapped),
        ]),
        new SliderOption("Intensity", 0, 1, () => Intensity, v => Intensity = v),
        new SliderOption("Outset", 0, 32, () => Outset, v => Outset = v, "0"),
        new ToggleOption("Accent edge", "Edge in the app's accent instead of the spectrum.", () => UseAccent, v => UseAccent = v));

    void OnEnded(object? sender, HighlightEndedEventArgs e)
    {
        if (e.Kind == HighlightKind.Spotlight)
            IsSpotlightOn = false;

        Log = $"{e.Kind} ended{(e.ByTap ? " by a tap" : "")}";
        UpdateHintState();
    }

    void UpdateHintState() => HintState = _highlights.HasSeen(HintKey) ? "Seen: the hint will not play again" : "Not seen yet: tap the button";
}
