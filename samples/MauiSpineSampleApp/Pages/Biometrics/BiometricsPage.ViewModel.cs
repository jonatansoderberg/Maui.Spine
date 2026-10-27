using Plugin.Maui.Spine.Biometrics;

namespace MauiSpineSampleApp.Pages.Biometrics;

public partial class BiometricsPageViewModel(INavigationService _navigation, IBiometricAuth _auth, IUnlock _unlock) : SampleViewModel
{
    /// <summary>The preference the app lock's <c>IsEnabled</c> reads in <c>MauiProgram</c>.</summary>
    public const string AppLockKey = "Showcase.AppLock";

    public bool AppLock
    {
        get => Preferences.Get(AppLockKey, false);
        set
        {
            Preferences.Set(AppLockKey, value);
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial string Availability { get; set; } = "";

    [ObservableProperty]
    public partial string Log { get; set; } = "Nothing yet";

    public MenuItems AccountMenu => [new MenuAction("Delete account", "delete.svg", DeleteAccountCommand) { IsDestructive = true }];

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {

        var availability = await _auth.GetAvailabilityAsync();
        Availability = $"{availability.Kind}, enrolled: {(availability.Enrolled ? "yes" : "no")}, passcode: {(availability.HasDeviceCredential ? "yes" : "no")}";
    }

    [RelayCommand]
    private async Task UnlockNow() =>
        Log = await _unlock.RequireAsync("Show what Spine can do") ? "Unlocked" : "Not unlocked";

    [RelayCommand]
    private void LockNow()
    {
        _unlock.Lock();
        Log = "Locked";
    }

    [RelayCommand]
    private Task OpenPin() => _navigation.NavigateToAsync<PinPage>();

    [RelayCommand]
    private Task OpenStatement() => _navigation.NavigateToAsync<StatementPage>();

    [RequiresUnlock("Delete the account", Fresh = true)]
    [RelayCommand]
    private void DeleteAccount() => Log = "Deleted the account";

    [RelayCommand]
    private void RevealCode() => Log = "The code is 4711";
}
