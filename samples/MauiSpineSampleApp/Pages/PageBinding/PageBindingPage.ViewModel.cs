namespace MauiSpineSampleApp.Pages.PageBinding;

public sealed record Fruit(string Name, decimal PricePerKg);

public partial class PageBindingPageViewModel : ViewModelBase
{
    public IReadOnlyList<Fruit> Fruits { get; } =
    [
        new("Apple", 29), new("Banana", 24), new("Cherry", 89), new("Damson", 45), new("Fig", 120), new("Grape", 59),
    ];

    // Page-level state that every row shows: the rows have no Currency of their own.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Currency))]
    public partial bool UseEuro { get; set; }

    public string Currency => UseEuro ? "EUR/kg" : "SEK/kg";

    [ObservableProperty]
    public partial string Picked { get; set; } = "Nothing picked yet";

    // A page-level command that a row's button runs with the row as parameter.
    [RelayCommand]
    private void Pick(Fruit fruit) => Picked = $"You picked {fruit.Name} ({fruit.PricePerKg:0} {Currency})";
}
