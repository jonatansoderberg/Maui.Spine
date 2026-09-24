namespace MauiSpineSampleApp.Pages.Theme;

public partial class ThemePageViewModel(IThemeService _theme) : ViewModelBase
{
    [ObservableProperty]
    public partial string SelectedThemeName { get; set; } = ThemeToName(_theme.Current);

    [ObservableProperty]
    public partial string Effective { get; set; } = Describe(_theme);

    partial void OnSelectedThemeNameChanged(string value) => _theme.Current = NameToTheme(value);

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        _theme.Changed += OnThemeChanged;
        Effective = Describe(_theme);
        return base.OnAppearingAsync(navigationDirection);
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _theme.Changed -= OnThemeChanged;
        return base.OnDisappearingAsync(navigationDirection);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Effective = Describe(_theme);

    private static string Describe(IThemeService theme) => $"Effective: {theme.Effective}, version {theme.Version}";

    public static string ThemeToName(AppTheme theme) => theme switch
    {
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        _ => "System",
    };

    public static AppTheme NameToTheme(string name) => name switch
    {
        "Light" => AppTheme.Light,
        "Dark" => AppTheme.Dark,
        _ => AppTheme.Unspecified,
    };
}
