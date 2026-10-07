namespace MauiSpineSampleApp.Pages.Transitions;

public partial class TransitionZoomPageViewModel : ViewModelBase, IReceivesNavigationParameter<TransitionTile>
{
    [ObservableProperty]
    public partial TransitionTile? Tile { get; set; }

    public Task OnNavigationParameterAsync(TransitionTile param)
    {
        Tile = param;
        Title = param.Title;
        return Task.CompletedTask;
    }
}
