namespace MauiSpineSampleApp.Pages.Rows;

public partial class RowsPageViewModel : ViewModelBase
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
    public partial string LastTap { get; set; } = "Nothing tapped yet";

    [ObservableProperty]
    public partial int CardTaps { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial bool CanOpen { get; set; } = true;

    private static readonly string[] Themes = ["System", "Light", "Dark"];

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open(string what) => LastTap = $"Opened {what} at {DateTime.Now:HH:mm:ss}";

    [RelayCommand]
    private void CycleTheme()
    {
        Theme = Themes[(Array.IndexOf(Themes, Theme) + 1) % Themes.Length];
        LastTap = $"Theme choice: {Theme}";
    }

    [RelayCommand]
    private void TapCard()
    {
        CardTaps++;
        LastTap = $"Card tapped {CardTaps} time{(CardTaps == 1 ? "" : "s")}";
    }
}
