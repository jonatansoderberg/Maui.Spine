namespace MauiSpineSampleApp.Pages;

public partial class SmallSheetPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [RelayCommand]
    private async Task Close() => await _navigation.BackAsync();
}
