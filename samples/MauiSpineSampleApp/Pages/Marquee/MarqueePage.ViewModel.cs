using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Marquee;

public partial class MarqueePageViewModel : SampleViewModel
{
    [ObservableProperty]
    public partial string DynamicText { get; set; } = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor.";

    [ObservableProperty]
    public partial double ScrollSpeed { get; set; } = 38;

    [ObservableProperty]
    public partial int PauseAtEnds { get; set; } = 2000;

    [ObservableProperty]
    public partial double FadeEdgeWidth { get; set; } = 8;

    [ObservableProperty]
    public partial bool FadeOnChange { get; set; } = true;

    [ObservableProperty]
    public partial bool RestartOnChange { get; set; }

    [ObservableProperty]
    public partial int Score { get; set; } = 98;

    [ObservableProperty]
    public partial string Countdown { get; set; } = "0:15";

    [ObservableProperty]
    public partial int RollDuration { get; set; } = 350;

    [ObservableProperty]
    public partial TextAlignment RollAlignment { get; set; } = TextAlignment.Start;

    [ObservableProperty]
    public partial AnimatedLabelSheen Sheen { get; set; } = AnimatedLabelSheen.Metal;

    [ObservableProperty]
    public partial int SheenInterval { get; set; } = 8000;

    [ObservableProperty]
    public partial int SheenDuration { get; set; } = 900;

    [ObservableProperty]
    public partial string? SheenIcon { get; set; } = "Crown.svg";

    [ObservableProperty]
    public partial double SheenSpacing { get; set; } = 1;

    private CancellationTokenSource? _countdown;

    private static readonly string[] _latinWords =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua",
    ];

    private static readonly Random _random = new();

    [RelayCommand]
    private void ChangeDynamicText()
    {
        var words = Enumerable.Range(0, _random.Next(3, 20))
            .Select(_ => _latinWords[_random.Next(_latinWords.Length)])
            .ToList();

        words[0] = char.ToUpper(words[0][0]) + words[0][1..];
        DynamicText = string.Join(" ", words) + ".";
    }

    [RelayCommand]
    private void Add(string amount) => Score += int.Parse(amount);

    [RelayCommand]
    private async Task CountDown()
    {
        _countdown?.Cancel();
        var cts = _countdown = new CancellationTokenSource();

        for (var left = TimeSpan.FromSeconds(15); left >= TimeSpan.Zero; left -= TimeSpan.FromSeconds(1))
        {
            Countdown = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
            try
            {
                await Task.Delay(1000, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [RelayCommand]
    private Task ShowScrollOptions() => ShowOptionsAsync("Scrolling",
        new SliderOption("Speed, points per second", 10, 150, () => ScrollSpeed, v => ScrollSpeed = v, "0"),
        new SliderOption("Pause at each end, ms", 0, 5000, () => PauseAtEnds, v => PauseAtEnds = (int)v, "0"),
        new SliderOption("Edge fade, points", 0, 40, () => FadeEdgeWidth, v => FadeEdgeWidth = v, "0"));

    [RelayCommand]
    private Task ShowChangeOptions() => ShowOptionsAsync("Changing text",
        new ToggleOption("Fade", "The old text fades out and the new text in.", () => FadeOnChange, v => FadeOnChange = v),
        new ToggleOption("Start over", "Scrolling restarts from the beginning of the new text. Off, it carries on from where it was.", () => RestartOnChange, v => RestartOnChange = v));

    [RelayCommand]
    private Task ShowRollOptions()
    {
        var alignment = new ChoiceGroup("Alignment",
        [
            new("Start", "A digit the number gains comes in at the end; the rest stay where they are.", () => RollAlignment = TextAlignment.Start),
            new("Center", "The number stays centred, so it shifts half a digit when it gains one.", () => RollAlignment = TextAlignment.Center),
            new("End", "The ones stay over the ones; a digit the number gains comes in at the front.", () => RollAlignment = TextAlignment.End),
        ]);
        alignment.Select((int)RollAlignment);

        return ShowOptionsAsync("Rolling",
            alignment,
            new SliderOption("Duration, ms", 100, 1500, () => RollDuration, v => RollDuration = (int)v, "0"));
    }

    [RelayCommand]
    private Task ShowSheenOptions()
    {
        var kind = new ChoiceGroup("Sheen",
        [
            new("Metal", "The colour as polished metal, lighter on top, and the glint.", () => Sheen = AnimatedLabelSheen.Metal),
            new("Glint", "The colour as it is, with the glint.", () => Sheen = AnimatedLabelSheen.Glint),
            new("None", "Flat colour, nothing moves.", () => Sheen = AnimatedLabelSheen.None),
        ]);
        kind.Select(Sheen switch { AnimatedLabelSheen.Metal => 0, AnimatedLabelSheen.Glint => 1, _ => 2 });

        return ShowOptionsAsync("Sheen",
            kind,
            new SliderOption("Every, seconds", 2, 20, () => SheenInterval / 1000d, v => SheenInterval = (int)(v * 1000), "0"),
            new SliderOption("Sweep, ms", 300, 2500, () => SheenDuration, v => SheenDuration = (int)v, "0"),
            new SliderOption("Letter spacing, points", 0, 4, () => SheenSpacing, v => SheenSpacing = v, "0.0"),
            new ToggleOption("Crown", "An icon before the text takes the metal and the glint with it.", () => SheenIcon is not null, v => SheenIcon = v ? "Crown.svg" : null));
    }
}
