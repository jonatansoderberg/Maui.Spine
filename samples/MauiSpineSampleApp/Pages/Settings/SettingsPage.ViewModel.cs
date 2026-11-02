using System.Globalization;

namespace MauiSpineSampleApp.Pages.Settings;

public partial class SettingsPageViewModel(INavigationService _navigation) : ViewModelBase
{
    private const string FeedbackUrl = "https://github.com/jonatansoderberg/Maui.Spine/issues/new";

    public string Version => AppInfo.Current.VersionString;

    // The build number is the minutes since 2026-01-01 UTC (MauiSpineSampleApp.csproj).
    public string BuildTime => int.TryParse(AppInfo.Current.BuildString, out var minutes)
        ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes).ToLocalTime().ToString("d MMMM yyyy HH:mm", CultureInfo.CurrentCulture)
        : "";

    [RelayCommand]
    private Task OpenAbout() => _navigation.NavigateToAsync<AboutPage>();

    // The Showcase is open source, so feedback is an issue on its repository rather than a mail.
    [RelayCommand]
    private Task SendFeedback() =>
        Launcher.Default.OpenAsync($"{FeedbackUrl}?title={Uri.EscapeDataString($"{AppInfo.Current.Name} {Version}: ")}");
}
