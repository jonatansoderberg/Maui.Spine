using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Rows;

public partial class RowsPageViewModel : SampleViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WiFiState))]
    public partial bool WiFi { get; set; } = true;

    public string WiFiState => WiFi ? "Connected" : "Off";

    [ObservableProperty]
    public partial bool Notifications { get; set; }

    [ObservableProperty]
    public partial string Theme { get; set; } = "System";

    [ObservableProperty]
    public partial string LastTap { get; set; } = "Tap a row.";

    [ObservableProperty]
    public partial int CardTaps { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial bool CanOpen { get; set; } = true;

    // The system colours of iOS Settings' icons.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlueBadge), nameof(RedBadge), nameof(IndigoBadge), nameof(GrayBadge))]
    public partial bool Badges { get; set; } = true;

    public Color? BlueBadge => Badges ? Color.FromArgb("#007AFF") : null;
    public Color? RedBadge => Badges ? Color.FromArgb("#FF3B30") : null;
    public Color? IndigoBadge => Badges ? Color.FromArgb("#5856D6") : null;
    public Color? GrayBadge => Badges ? Color.FromArgb("#8E8E93") : null;

    [ObservableProperty]
    public partial SpineRowValuePlacement Placement { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortByName), nameof(SortByDate), nameof(SortBySize))]
    public partial string SortBy { get; set; } = "Name";

    public bool SortByName => SortBy == "Name";
    public bool SortByDate => SortBy == "Date";
    public bool SortBySize => SortBy == "Size";

    private static readonly string[] Themes = ["System", "Light", "Dark"];

    [RelayCommand]
    private Task ShowRowOptions()
    {
        var placement = new ChoiceGroup("Value placement",
        [
            new("Auto", "The platform's place: under the title on Android, at the end of the row elsewhere.", () => Placement = SpineRowValuePlacement.Auto),
            new("Trailing", "At the end of the row, as in iOS Settings.", () => Placement = SpineRowValuePlacement.Trailing),
            new("Below", "Under the title, as a preference's summary in Android's settings.", () => Placement = SpineRowValuePlacement.Below),
        ]);
        placement.Select((int)Placement);

        return ShowOptionsAsync("Rows",
            new ToggleOption("Icon badges", "White icons on coloured squares, as in iOS Settings (circles on Android). Off: the icon tinted with the accent.", () => Badges, value => Badges = value),
            placement,
            new ToggleOption("Rows can open", "Follows CanExecute of the rows' command: off, Account and Privacy give no press feedback and take no taps.", () => CanOpen, value => CanOpen = value));
    }

    [RelayCommand]
    private void Sort(string by) => SortBy = by;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open(string what) => LastTap = $"Opened {what} at {DateTime.Now:HH:mm:ss}";

    [RelayCommand]
    private void CycleTheme()
    {
        Theme = Themes[(Array.IndexOf(Themes, Theme) + 1) % Themes.Length];
        LastTap = $"Theme choice: {Theme}";
    }

    [RelayCommand]
    private void TapCard() => CardTaps++;

    [ObservableProperty]
    public partial string? Email { get; set; }

    [ObservableProperty]
    public partial string? Password { get; set; }

    [ObservableProperty]
    public partial string SignInState { get; set; } = "Type an email and a password.";

    [RelayCommand]
    private void SignIn() => SignInState = string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Password)
        ? "Both fields are needed."
        : $"Would sign in {Email} with a {Password.Length}-character password.";
}
