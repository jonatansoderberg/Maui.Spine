namespace MauiSpineSampleApp.Pages;

public sealed record FullscreenSheetResult(string Message);

public partial class FullscreenSheetPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [RelayCommand]
    private async Task Confirm() =>
        await _navigation.ReturnAsync(new FullscreenSheetResult("Confirmed!"));

    // A null result is still a result: the caller sees IsSuccess = true with no value.
    [RelayCommand]
    private async Task ReturnNull() =>
        await _navigation.ReturnAsync(null);

    // No result: the caller sees a canceled outcome, the same as a swipe-down.
    [RelayCommand]
    private async Task Cancel() =>
        await _navigation.CloseAsync();
}
