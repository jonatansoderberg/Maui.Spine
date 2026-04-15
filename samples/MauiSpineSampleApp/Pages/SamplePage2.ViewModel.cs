namespace MauiSpineSampleApp.Pages;

public partial class SamplePage2ViewModel(INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty]
    public partial string? UserName { get; set; }

    [RelayCommand]
    private async Task Back() => await _navigation.BackAsync();

    [RelayCommand]
    private Task DummyAction() => Task.CompletedTask;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
        {
            PageActions.Add(new PageAction(text: null, command: DummyActionCommand)
            {
                Svg = "bus.svg"
            });
        }

        return base.OnAppearingAsync(navigationDirection);
    }
}
