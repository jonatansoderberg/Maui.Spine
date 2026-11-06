using System.Globalization;

namespace MauiSpineSampleApp.Pages.Settings;

public partial class SettingsPageViewModel : ViewModelBase
{
    private const string FeedbackUrl = "https://github.com/jonatansoderberg/Maui.Spine/issues/new";

    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly MenuPicker _themePicker;

    public SettingsPageViewModel(INavigationService navigation, IThemeService theme)
    {
        _navigation = navigation;
        _theme = theme;

        _themePicker = new MenuPicker(PickThemeCommand)
        {
            new MenuAction("System", "auto.svg") { CommandParameter = AppTheme.Unspecified },
            new MenuAction("Light", "weather1.svg") { CommandParameter = AppTheme.Light },
            new MenuAction("Dark", "weather1n.svg") { CommandParameter = AppTheme.Dark },
        };

        ThemeMenu = [_themePicker];
        Accents = [.. SampleAccent.All.Select(a => new AccentSwatch(a))];

        // The swatches show the accent as it looks in the current theme, which System changes by itself.
        WhileVisible(() =>
        {
            SyncPickers();
            _theme.Changed += OnThemeChanged;
            Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
        }, () =>
        {
            _theme.Changed -= OnThemeChanged;
            Application.Current!.RequestedThemeChanged -= OnRequestedThemeChanged;
        });
        SyncPickers();
    }

    public MenuItems ThemeMenu { get; }

    /// <summary>The accents as colour swatches, one tap to pick.</summary>
    public IReadOnlyList<AccentSwatch> Accents { get; }

    public string? AccentName => Accents.FirstOrDefault(a => a.IsSelected)?.Name;

    // major.yyMMdd.revision and the build id, minutes since 2026-01-01 UTC (Version.props).
    public string Version => $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    // The day only: a time does not fit beside the version. Abbreviated months end in a dot in some languages.
    public string BuildDate => int.TryParse(AppInfo.Current.BuildString, out var minutes)
        ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes).ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture).Replace(".", "")
        : "";

    [RelayCommand]
    private void PickTheme(AppTheme theme) => _theme.Current = theme;

    [RelayCommand]
    private void PickAccent(AccentSwatch swatch) => _theme.Accent = swatch.Accent.Accent;

    private void OnThemeChanged(object? sender, EventArgs e) => SyncPickers();

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) => SyncPickers();

    private void SyncPickers()
    {
        _themePicker.Selected = _themePicker.Items.First(a => Equals(a.CommandParameter, _theme.Current));

        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        foreach (var swatch in Accents)
            swatch.Update(Equals(swatch.Accent.Accent, _theme.Accent), dark);
        OnPropertyChanged(nameof(AccentName));
    }

    [RelayCommand]
    private Task OpenAbout() => _navigation.NavigateToAsync<AboutPage>();

    // The Showcase is open source, so feedback is an issue on its repository rather than a mail.
    [RelayCommand]
    private Task SendFeedback() =>
        Launcher.Default.OpenAsync($"{FeedbackUrl}?title={Uri.EscapeDataString($"{AppInfo.Current.Name} {Version}: ")}");
}

/// <summary>One accent on the settings page: its colour in the current theme, and whether it is the one in use.</summary>
public sealed partial class AccentSwatch(SampleAccent accent) : ObservableObject
{
    public SampleAccent Accent { get; } = accent;

    public string Name => Accent.Name;

    [ObservableProperty]
    public partial Brush? Fill { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    public partial bool IsSelected { get; private set; }

    public string Description => IsSelected ? $"{Name}, selected" : Name;

    public void Update(bool selected, bool dark)
    {
        IsSelected = selected;
        Fill = new SolidColorBrush(dark ? Accent.Dark : Accent.Light);
    }
}
