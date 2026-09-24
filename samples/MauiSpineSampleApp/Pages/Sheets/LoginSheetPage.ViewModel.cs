namespace MauiSpineSampleApp.Pages.Sheets;

public partial class LoginSheetPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [RelayCommand]
    private async Task LogIn() => await _navigation.CloseAsync();
}
