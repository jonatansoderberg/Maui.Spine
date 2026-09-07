using Plugin.Maui.Spine.Widgets;

namespace MauiSpineSampleApp.Pages.Settings;

public partial class SettingsPageViewModel(IWidgetService _widgets, ILiveActivityService _liveActivities) : ViewModelBase
{
    private const string ActivityKind = "sample";

    [ObservableProperty]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    public partial string LiveActivityLabel { get; set; } = "Start live activity";

    // The running activity is asked for rather than held: an activity outlives the page, and the app.
    private LiveActivity? Activity => _liveActivities.Active.FirstOrDefault(a => a.Kind == ActivityKind);

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        LiveActivityLabel = Activity is null ? "Start live activity" : "End live activity";
        return base.OnAppearingAsync(navigationDirection);
    }

    [ObservableProperty]
    public partial string SelectedThemeName { get; set; } = ThemeToName(Application.Current?.UserAppTheme ?? AppTheme.Unspecified);

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

    [RelayCommand]
    private Task RefreshWidgets() => _widgets.RefreshAllAsync();

    // Demo of the Live Activity API: one countdown in the Dynamic Island, ended by the same button.
    [RelayCommand]
    private async Task ToggleLiveActivity()
    {
        if (Activity is { } running)
        {
            await running.EndAsync();
            LiveActivityLabel = "Start live activity";
            return;
        }

        var start = DateTimeOffset.Now.AddMinutes(42);
        var activity = await _liveActivities.StartAsync(ActivityKind, new LiveActivityLayout
        {
            LockScreen = W.HStack(10,
                W.Icon("fish", WidgetColor.Green),
                W.VStack(2, W.Text("Sthlm Indoor Cup · H21").Headline().Bold(), W.Text("Your start").Caption().Secondary()),
                W.Spacer(),
                W.Timer(start).Title().Bold().Color(WidgetColor.Green)),
            ExpandedLeading = W.Icon("fish", WidgetColor.Green),
            ExpandedTrailing = W.Timer(start).Headline().Bold().Color(WidgetColor.Green),
            ExpandedCenter = W.Text("Sthlm Indoor Cup · H21").Headline().Bold(),
            ExpandedBottom = W.VStack(W.Text("Start 11:04 · Course 6.3 km").Caption().Secondary(), W.Progress(0.35, WidgetColor.Green)),
            CompactLeading = W.Icon("fish", WidgetColor.Green),
            CompactTrailing = W.Timer(start).Caption().Bold().Color(WidgetColor.Green),
            Minimal = W.Icon("fish", WidgetColor.Green),
            Link = _widgets.LinkFor(ActivityKind),
        }, staleAt: start.AddHours(1));

        LiveActivityLabel = activity is null ? "Live activities unavailable" : "End live activity";
    }
}
