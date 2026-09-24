using System.ComponentModel;

namespace MauiSpineSampleApp.Pages.ScrollEdge;

public partial class ScrollEdgePageViewModel : ViewModelBase
{
    static readonly string[] Palette = ["#E4572E", "#F3A712", "#29335C", "#669BBC", "#A8C686", "#7B2D26"];

    // Colourful cards with text on them, so what passes under the bar is easy to see through the edge.
    public IReadOnlyList<Swatch> Rows { get; } = Enumerable.Range(1, 30)
        .Select(i => new Swatch(
            $"Row {i}",
            i % 2 == 0 ? "Text under the bar should blur into it, not run through the title." : "Colours show how much of the row the band lets through.",
            Color.FromArgb(Palette[i % Palette.Length])))
        .ToList();

    public IReadOnlyList<BackgroundChoice> Backgrounds { get; }

    public ScrollEdgePageViewModel()
    {
        Backgrounds =
        [
            new("Auto", HeaderBarBackground.Auto, Choose),
            new("Solid", HeaderBarBackground.Solid, Choose),
            new("Clear", HeaderBarBackground.Clear, Choose),
            new("ScrollEdge", HeaderBarBackground.ScrollEdge, Choose),
            new("Soft", HeaderBarBackground.ScrollEdgeSoft, Choose),
            new("Hard", HeaderBarBackground.ScrollEdgeHard, Choose),
        ];
    }

    void Choose(HeaderBarBackground value) => HeaderBarBackground = value;

    // HeaderBarBackground and HeaderBarMode start as the attribute says; setting them lays the page out again, live.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(HeaderBarBackground))
        {
            foreach (var choice in Backgrounds)
                choice.IsSelected = choice.Value == HeaderBarBackground;
        }
        else if (e.PropertyName == nameof(HeaderBarMode))
            OnPropertyChanged(nameof(Overlay));
    }

    public bool Overlay
    {
        get => HeaderBarMode == HeaderBarMode.Overlay;
        set => HeaderBarMode = value ? HeaderBarMode.Overlay : HeaderBarMode.Normal;
    }
}

public sealed record Swatch(string Name, string Text, Color Colour);

public sealed partial class BackgroundChoice(string label, HeaderBarBackground value, Action<HeaderBarBackground> choose) : ObservableObject
{
    public string Label { get; } = label;

    public HeaderBarBackground Value { get; } = value;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Choose() => choose(Value);
}
