namespace MauiSpineSampleApp.Pages;

public sealed record FullscreenSheetResult(string Message);

public partial class FullscreenSheetPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [RelayCommand]
    private async Task Confirm() =>
        await _navigation.ReturnAsync(new FullscreenSheetResult("Confirmed!"));

    [RelayCommand]
    private async Task Cancel() =>
        await _navigation.BackAsync();
}
