namespace MauiSpineSampleApp.Pages.Settings;

public partial class SettingsPageViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    public partial string SelectedThemeName { get; set; }

    public SettingsPageViewModel()
    {
        SelectedThemeName = ThemeToName(Application.Current?.UserAppTheme ?? AppTheme.Unspecified);
    }

    partial void OnSelectedThemeNameChanged(string value)
    {
        if (Application.Current is not null)
            Application.Current.UserAppTheme = NameToTheme(value);
    }

    private static string ThemeToName(AppTheme theme) => theme switch
    {
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        _ => "Auto"
    };

    private static AppTheme NameToTheme(string name) => name switch
    {
        "Light" => AppTheme.Light,
        "Dark" => AppTheme.Dark,
        _ => AppTheme.Unspecified
    };

    [RelayCommand]
    private void Change()
    {
        UserName = DateTime.Now.ToString(); 
    }
}
