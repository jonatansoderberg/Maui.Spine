namespace MauiSpineSampleApp.Pages.PageBinding;

public partial class PageBindingPageViewModel : ViewModelBase
{
    public IReadOnlyList<string> Fruits { get; } = ["Apple", "Banana", "Cherry", "Damson", "Elderberry", "Fig", "Grape"];

    [ObservableProperty]
    public partial string Picked { get; set; } = "Nothing picked yet";

    [ObservableProperty]
    public partial int Picks { get; set; }

    // Reached from inside the item template with {PageCommand Pick}.
    [RelayCommand]
    private void Pick(string fruit)
    {
        Picks++;
        Picked = $"{fruit} (pick #{Picks})";
    }
}
