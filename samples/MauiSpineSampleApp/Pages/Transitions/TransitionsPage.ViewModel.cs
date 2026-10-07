using System.ComponentModel;

namespace MauiSpineSampleApp.Pages.Transitions;

public partial class TransitionsPageViewModel : SampleViewModel
{
    private readonly INavigationService _navigation;
    private readonly ChoiceGroup _kind;

    public TransitionsPageViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _kind = new("Transition",
        [
            new("Shared element", "The tile flies to the card on the page it opens while the pages slide, and back into the grid on the way back. One tag on each.", () => Kind = TransitionKind.SharedElement),
            new("Zoom", "The page grows out of the tile, its card lined up with it, and shrinks back into it; the back-swipe shrinks it under the finger. The tag on the page itself, and on its card.", () => Kind = TransitionKind.Zoom),
            new("Zoom without a focus", "The page grows out of the tile about its middle. The tag on the page only, for a page with nothing that looks like the tile.", () => Kind = TransitionKind.ZoomWithoutFocus),
        ]);
        _kind.Select(0);
    }

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

    public ChoiceGroup KindGroup => _kind;

    [ObservableProperty]
    public partial TransitionKind Kind { get; set; }

    [RelayCommand]
    private Task ShowOptions() => ShowOptionsAsync("Transitions", _kind);

    [RelayCommand]
    private Task Open(TransitionTile tile) =>
        _navigation.NavigateToAsync<TransitionDetailPage, TransitionTarget>(new(tile, Kind));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(Kind))
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Code)));
    }

    /// <summary>The XAML for the transition chosen.</summary>
    public string Code => Kind switch
    {
        TransitionKind.SharedElement => """
            <!-- The grid -->
            <Border Transition.Tag="{Binding Key}" ... />

            <!-- The page it opens: the same tag
                 on the view the tile becomes -->
            <Border
                Transition.Tag="{Binding Tile.Key}" ... />
            """,
        TransitionKind.Zoom => """
            <!-- The grid -->
            <Border Transition.Tag="{Binding Key}" ... />

            <!-- The page it opens: the tag on the page
                 itself zooms it out of the tile... -->
            <SpinePage ...
                Transition.Tag="{Binding Tile.Key}">

              <!-- ...and on its card lines the card
                   up with the tile -->
              <Border
                Transition.Tag="{Binding Tile.Key}" ... />
            """,
        _ => """
            <!-- The grid -->
            <Border Transition.Tag="{Binding Key}" ... />

            <!-- The page it opens: the tag on the page
                 itself only; its middle lines up -->
            <SpinePage ...
                Transition.Tag="{Binding Tile.Key}">
            """,
    };
}

public sealed record TransitionTile(int Id, string Title, string Icon, Color Color)
{
    /// <summary>The tile's transition tag: the same on the tile and on what it opens.</summary>
    public string Key => $"tile-{Id}";
}

public enum TransitionKind { SharedElement, Zoom, ZoomWithoutFocus }

/// <summary>The tile a detail page shows, and the transition it was opened with.</summary>
public sealed record TransitionTarget(TransitionTile Tile, TransitionKind Kind);
