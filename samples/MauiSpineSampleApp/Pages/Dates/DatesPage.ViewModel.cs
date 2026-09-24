using System.Globalization;
using MauiSpineSampleApp.Pages.Theme;
using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Pages.Dates;

public partial class DatesPageViewModel(IThemeService _theme, ISpineStrings _strings) : ViewModelBase
{
    // Both repaint the calendar while it is on screen, through SpineTheme.Track.
    [ObservableProperty]
    public partial string ThemeName { get; set; } = ThemePageViewModel.ThemeToName(_theme.Current);

    [ObservableProperty]
    public partial string Language { get; set; } = _strings.Culture.TwoLetterISOLanguageName == "sv" ? "sv" : "en";

    partial void OnThemeNameChanged(string value) => _theme.Current = ThemePageViewModel.NameToTheme(value);

    partial void OnLanguageChanged(string value) => _strings.Culture = CultureInfo.GetCultureInfo(value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    public partial DateTime SelectedDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial DateTime DisplayDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial bool ShowWeekNumbers { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowTrailingDays { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSelectedDate { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstDayOfWeek))]
    public partial string FirstDay { get; set; } = nameof(DayOfWeek.Monday);

    public DayOfWeek FirstDayOfWeek => Enum.Parse<DayOfWeek>(FirstDay);

    /// <summary>"App" follows SpineStrings (the Strings page's language switch); the rest pin a culture.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Culture))]
    public partial string CultureName { get; set; } = "App";

    public CultureInfo? Culture => CultureName == "App" ? null : CultureInfo.GetCultureInfo(CultureName);

    public string SelectedText => SelectedDate == DateTime.MinValue ? "No selection" : $"Selected: {SelectedDate:yyyy-MM-dd}";

    public string DisplayText => $"Showing: {DisplayDate:yyyy-MM}";

    [RelayCommand] private void GoToToday() => SelectedDate = DateTime.Today;

    [RelayCommand] private void ClearSelection() => SelectedDate = DateTime.MinValue;
}
