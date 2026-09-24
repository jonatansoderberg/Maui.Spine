namespace MauiSpineSampleApp.Pages.ScrollEdge;

public partial class ScrollEdgePageViewModel : ViewModelBase
{
    static readonly string[] Palette = ["#E4572E", "#F3A712", "#29335C", "#669BBC", "#A8C686", "#7B2D26"];

    // Colourful rows, so what passes under the bar is easy to see through the edge.
    public IReadOnlyList<Swatch> Rows { get; } = Enumerable.Range(1, 30)
        .Select(i => new Swatch($"Row {i}", Color.FromArgb(Palette[i % Palette.Length])))
        .ToList();
}

public sealed record Swatch(string Name, Color Colour);
