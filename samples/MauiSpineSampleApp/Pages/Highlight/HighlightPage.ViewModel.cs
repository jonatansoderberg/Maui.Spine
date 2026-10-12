using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Highlight;

public sealed partial class SyncRow(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsSyncing { get; set; }
}

public partial class HighlightPageViewModel(IHighlights _highlights) : SampleViewModel
{
    /// <summary>The key of the one-off hint; "Show tips again" forgets it.</summary>
    public const string HintKey = "showcase.highlight.add";

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
        return Task.CompletedTask;
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
        Log = $"{e.Kind} ended{(e.ByTap ? " by a tap" : "")}";
        UpdateHintState();
    }

    void UpdateHintState() => HintState = _highlights.HasSeen(HintKey) ? "Seen: the hint will not play again" : "Not seen yet: tap the button";
}
