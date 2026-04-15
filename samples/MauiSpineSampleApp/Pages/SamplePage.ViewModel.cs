namespace MauiSpineSampleApp.Pages;

public partial class SamplePageViewModel(INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty]
    public partial string? UserName { get; set; }

    [RelayCommand]
    private async Task Next() => await _navigation.NavigateToAsync<SamplePage2>();

    [RelayCommand]
    private Task DummyAction() => Task.CompletedTask;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
        {
            PageActions.Add(new PageAction(text: "Save", command: DummyActionCommand)
            {
                //Svg = "car.svg"
            });
        }

        return base.OnAppearingAsync(navigationDirection);
    }
}
