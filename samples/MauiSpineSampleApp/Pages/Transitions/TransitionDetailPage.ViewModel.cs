namespace MauiSpineSampleApp.Pages.Transitions;

public partial class TransitionDetailPageViewModel : ViewModelBase, IReceivesNavigationParameter<TransitionTarget>
{
    [ObservableProperty]
    public partial TransitionTile? Tile { get; set; }

    // The tile's tag on the page itself zooms the page; on the card, the card is what the tile
    // becomes (a shared element), or what lines up with it in a zoom.
    [ObservableProperty]
    public partial string? PageTag { get; set; }

    [ObservableProperty]
    public partial string? CardTag { get; set; }

    [ObservableProperty]
    public partial string? Text { get; set; }

    public Task OnNavigationParameterAsync(TransitionTarget param)
    {
        var tag = param.Tile.Key;

        Tile = param.Tile;
        Title = param.Tile.Title;
        PageTag = param.Kind is TransitionKind.SharedElement ? null : tag;
        CardTag = param.Kind is TransitionKind.ZoomWithoutFocus ? null : tag;
        Text = param.Kind switch
        {
            TransitionKind.SharedElement => "The tile flew here from the grid and became this card. Go back with the back button and it flies back into its place; swipe back from the edge and the page slides away as usual.",
            TransitionKind.Zoom => "This page grew out of the tile, with this card lined up with it. Go back with the back button and it shrinks back into the grid as the tile; swipe back from the edge and it shrinks under your finger.",
            _ => "This page grew out of the tile about its middle: nothing on it carries the tile's tag but the page itself. Go back and it shrinks back into the grid.",
        };

        return Task.CompletedTask;
    }
}
