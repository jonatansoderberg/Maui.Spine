namespace MauiSpineSampleApp.Pages;

public partial class SamplePage2ViewModel(INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty]
    public partial string? UserName { get; set; }

    [RelayCommand]
    private async Task Back() => await _navigation.BackAsync();

    [PageAction(Svg = "bus.svg")]
    [RelayCommand]
    private Task DummyAction() => Task.CompletedTask;
}
