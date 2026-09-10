namespace MauiSpineSampleApp.Pages.Glass;

public partial class GlassPageViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string LastTap { get; set; } = "Tap a button";

    [ObservableProperty]
    public partial bool IsSaveEnabled { get; set; } = true;

    [RelayCommand]
    private void Tap(string? name) => LastTap = $"{name} tapped at {DateTime.Now:HH:mm:ss}";

    [RelayCommand]
    private void ToggleSave() => IsSaveEnabled = !IsSaveEnabled;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
        {
            PageActions.Add(new PageAction(text: null, command: TapCommand) { Svg = "bell.svg", CommandParameter = "Bell" });
        }

        return base.OnAppearingAsync(navigationDirection);
    }
}
