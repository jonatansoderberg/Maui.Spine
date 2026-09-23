namespace MauiSpineSampleApp.Pages.Glass;

public partial class GlassPageViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string LastTap { get; set; } = "Tap a button";

    [ObservableProperty]
    public partial bool IsSaveEnabled { get; set; } = true;

    [RelayCommand]
    private void Tap(string? name) => LastTap = $"{name} tapped at {DateTime.Now:HH:mm:ss}";

    // A declared action needs a parameterless command; the header bell reports itself.
    [PageAction(Svg = "bell.svg")]
    [RelayCommand]
    private void Bell() => Tap("Bell");

    [RelayCommand]
    private void ToggleSave() => IsSaveEnabled = !IsSaveEnabled;
}
