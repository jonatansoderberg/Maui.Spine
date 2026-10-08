using System.ComponentModel;

namespace MauiSpineSampleApp.Pages.Search;

public sealed record Town(string Name, string County);

public partial class SearchPageViewModel(INavigationService navigation) : SampleViewModel
{
    private static readonly Town[] AllTowns =
    [
        new("Arvika", "Värmland"), new("Borås", "Västra Götaland"), new("Enköping", "Uppsala"),
        new("Eskilstuna", "Södermanland"), new("Falun", "Dalarna"), new("Gävle", "Gävleborg"),
        new("Göteborg", "Västra Götaland"), new("Halmstad", "Halland"), new("Helsingborg", "Skåne"),
        new("Hudiksvall", "Gävleborg"), new("Härnösand", "Västernorrland"), new("Jönköping", "Jönköping"),
        new("Kalmar", "Kalmar"), new("Karlskrona", "Blekinge"), new("Karlstad", "Värmland"),
        new("Kiruna", "Norrbotten"), new("Kristianstad", "Skåne"), new("Linköping", "Östergötland"),
        new("Luleå", "Norrbotten"), new("Lund", "Skåne"), new("Malmö", "Skåne"),
        new("Mora", "Dalarna"), new("Norrköping", "Östergötland"), new("Nyköping", "Södermanland"),
        new("Sala", "Västmanland"), new("Skellefteå", "Västerbotten"), new("Stockholm", "Stockholm"),
        new("Sundsvall", "Västernorrland"), new("Uddevalla", "Västra Götaland"), new("Umeå", "Västerbotten"),
        new("Uppsala", "Uppsala"), new("Visby", "Gotland"), new("Västerås", "Västmanland"),
        new("Växjö", "Kronoberg"), new("Ystad", "Skåne"), new("Örebro", "Örebro"),
        new("Örnsköldsvik", "Västernorrland"), new("Östersund", "Jämtland"),
    ];

    // The field in the header bar writes this property, and the list follows it like any other.
    [PageSearch(Placeholder = "Search towns", Submit = nameof(OpenFirstCommand))]
    [ObservableProperty]
    public partial string Query { get; set; } = "";

    partial void OnQueryChanged(string value)
    {
        Towns = string.IsNullOrWhiteSpace(value)
            ? AllTowns
            : AllTowns.Where(t => t.Name.Contains(value.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || t.County.Contains(value.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial IReadOnlyList<Town> Towns { get; private set; } = AllTowns;

    public string CountText => Towns.Count == AllTowns.Length ? $"{AllTowns.Length} towns" : $"{Towns.Count} of {AllTowns.Length} towns";

    [ObservableProperty]
    public partial string Submitted { get; set; } = "Press the keyboard's search key";

    public bool ShowsSearch
    {
        get => Search?.IsVisible ?? false;
        set
        {
            if (Search is null || Search.IsVisible == value)
                return;

            Search.IsVisible = value;
            OnPropertyChanged();
        }
    }

    public bool AlwaysInRow
    {
        get => Search?.Placement == SearchPlacement.Top;
        set
        {
            if (Search is null || AlwaysInRow == value)
                return;

            Search.Placement = value ? SearchPlacement.Top : SearchPlacement.Automatic;
            OnPropertyChanged();
        }
    }

    // Spine creates Search from the attribute just before the page first appears.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(Search))
        {
            OnPropertyChanged(nameof(ShowsSearch));
            OnPropertyChanged(nameof(AlwaysInRow));
        }
    }

    [RelayCommand]
    private void OpenFirst(string text)
    {
        // The keyboard goes away and the search goes on, with its results, until the X ends it.
        Submitted = Towns.FirstOrDefault() is { } first ? $"“{text}”: {first.Name} first" : $"“{text}”: no town";
    }

    [RelayCommand]
    private void StartSearch()
    {
        if (Search is not null)
            Search.IsActive = true;
    }

    [RelayCommand]
    private void EndSearch()
    {
        if (Search is not null)
            Search.IsActive = false;
    }

    [RelayCommand]
    private void Clear() => Query = "";

    [RelayCommand]
    private Task OpenSheet() => navigation.NavigateToAsync<SearchSheetPage>();
}

public partial class SearchSheetPageViewModel : SearchPageViewModel
{
    public SearchSheetPageViewModel(INavigationService navigation) : base(navigation)
    {
        PageActions.Remove(PageActions.Single(a => a.Menu == ThemeMenu));
    }
}
