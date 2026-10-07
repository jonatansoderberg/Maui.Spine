namespace MauiSpineSampleApp.Pages.Transitions;

public partial class TransitionsPageViewModel(INavigationService navigation) : SampleViewModel
{
    public IReadOnlyList<TransitionTile> Tiles { get; } =
    [
        new(1, "Sunrise", "weather1.svg", Color.FromArgb("#F2994A")),
        new(2, "Moon", "weather1n.svg", Color.FromArgb("#5B5FC7")),
        new(3, "Day length", "timer.svg", Color.FromArgb("#27AE60")),
        new(4, "Name days", "calendar.svg", Color.FromArgb("#EB5757")),
        new(5, "Week", "stack.svg", Color.FromArgb("#2D9CDB")),
        new(6, "Weather", "water.svg", Color.FromArgb("#56CCF2")),
        new(7, "Battery", "energy.svg", Color.FromArgb("#219653")),
        new(8, "Steps", "vertical.svg", Color.FromArgb("#9B51E0")),
        new(9, "Alarm", "bell.svg", Color.FromArgb("#F2C94C")),
        new(10, "Notes", "edit.svg", Color.FromArgb("#828282")),
        new(11, "Language", "globe.svg", Color.FromArgb("#BB6BD9")),
        new(12, "Photos", "image.svg", Color.FromArgb("#E07A5F")),
    ];

    // Zoom: the page opened has the tile's tag on itself and grows out of it; otherwise the tile flies to the page.
    [ObservableProperty]
    public partial bool Zoom { get; set; }

    [RelayCommand]
    private Task Open(TransitionTile tile) => Zoom
        ? navigation.NavigateToAsync<TransitionZoomPage, TransitionTile>(tile)
        : navigation.NavigateToAsync<TransitionDetailPage, TransitionTile>(tile);
}

public sealed record TransitionTile(int Id, string Title, string Icon, Color Color);
