namespace MauiSpineSampleApp.Pages.Sheets;

public partial class SheetsPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [RelayCommand] private async Task ShowBottomSheet() => await _navigation.NavigateToAsync<SamplePage>();
    [RelayCommand] private async Task ShowSimpleBottomSheet() => await _navigation.NavigateToAsync<SimpleBottomSheetPage>();
    [RelayCommand] private async Task ShowFullscreenSheet() => await _navigation.NavigateToAsync<FullscreenSheetPage>();
    [RelayCommand] private async Task ShowSmallSheet() => await _navigation.NavigateToAsync<SmallSheetPage>();
    [RelayCommand] private async Task ShowToggleList() => await _navigation.NavigateToAsync<ToggleListSheet>();
    [RelayCommand] private async Task ShowLoginSheet() => await _navigation.NavigateToAsync<LoginSheetPage>();
    [RelayCommand] private async Task ShowEditSheet() => await _navigation.NavigateToAsync<EditSheetPage>();
}
