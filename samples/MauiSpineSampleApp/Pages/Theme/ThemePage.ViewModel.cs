namespace MauiSpineSampleApp.Pages.Theme;

public partial class ThemePageViewModel : ViewModelBase
{
    private readonly IThemeService _theme;

    public ThemePageViewModel(IThemeService theme)
    {
        _theme = theme;
        SelectedThemeName = ThemeToName(theme.Current);
        Effective = Describe(theme);

        // Apple's system colours, light and dark. Blue is the sample's own accent (Primary and
        // PrimaryDark in Colors.xaml), so picking it clears IThemeService.Accent.
        Accents =
        [
            new("Blue", null, "#007AFF", "#0A84FF", Select),
            new("Indigo", "#5856D6", "#5E5CE6", Select),
            new("Purple", "#AF52DE", "#BF5AF2", Select),
            new("Pink", "#FF2D55", "#FF375F", Select),
            new("Red", "#FF3B30", "#FF453A", Select),
            new("Orange", "#FF9500", "#FF9F0A", Select),
            new("Yellow", "#FFCC00", "#FFD60A", Select),
            new("Green", "#34C759", "#30D158", Select),
            new("Mint", "#00C7BE", "#63E6E2", Select),
            new("Teal", "#30B0C7", "#40CBE0", Select),
        ];

        PaintAccents();
    }

    [ObservableProperty]
    public partial string SelectedThemeName { get; set; }

    [ObservableProperty]
    public partial string Effective { get; set; }

    [ObservableProperty]
    public partial string AccentName { get; set; } = "";

    public IReadOnlyList<AccentSwatch> Accents { get; }

    partial void OnSelectedThemeNameChanged(string value) => _theme.Current = NameToTheme(value);

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        _theme.Changed += OnThemeChanged;
        OnThemeChanged(this, EventArgs.Empty);
        return base.OnAppearingAsync(navigationDirection);
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _theme.Changed -= OnThemeChanged;
        return base.OnDisappearingAsync(navigationDirection);
    }

    private void Select(AccentSwatch swatch) => _theme.Accent = swatch.Accent;

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Effective = Describe(_theme);
        PaintAccents();
    }

    private void PaintAccents()
    {
        foreach (var swatch in Accents)
        {
            swatch.Paint(_theme.Effective);
            swatch.IsSelected = Equals(swatch.Accent, _theme.Accent);

            if (swatch.IsSelected)
                AccentName = swatch.Accent is null ? $"Accent: {swatch.Name}, the app's default" : $"Accent: {swatch.Name}";
        }
    }

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

/// <summary>One round accent swatch on the Theme page.</summary>
public sealed partial class AccentSwatch : ObservableObject
{
    private readonly Color _light;
    private readonly Color _dark;

    public AccentSwatch(string name, string light, string dark, Action<AccentSwatch> select)
        : this(name, new SpineAccent(Color.FromArgb(light), Color.FromArgb(dark)), light, dark, select)
    {
    }

    public AccentSwatch(string name, SpineAccent? accent, string light, string dark, Action<AccentSwatch> select)
    {
        Name = name;
        Accent = accent;
        _light = Color.FromArgb(light);
        _dark = Color.FromArgb(dark);
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Name { get; }

    /// <summary>What <see cref="IThemeService.Accent"/> is set to; null for the app's default.</summary>
    public SpineAccent? Accent { get; }

    public IRelayCommand SelectCommand { get; }

    [ObservableProperty]
    public partial Color Fill { get; set; } = Colors.Transparent;

    [ObservableProperty]
    public partial Brush Ring { get; set; } = Brush.Transparent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    public partial bool IsSelected { get; set; }

    public string Description => IsSelected ? $"{Name}, selected" : Name;

    public void Paint(AppTheme theme)
    {
        Fill = theme == AppTheme.Dark ? _dark : _light;
        Ring = new SolidColorBrush(Fill);
    }
}
