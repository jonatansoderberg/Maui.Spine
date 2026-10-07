using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MauiSpineSampleApp.Pages.DataGrid;

public partial class DataGridPageViewModel : SampleViewModel
{
    private const int PageSize = 30;
    private static readonly List<Product> Catalogue = Product.Generate(120);

    public ObservableCollection<Product> Products { get; } = new(Catalogue.Take(PageSize));

    // How far into the catalogue the pages reach; a deleted row does not move it back.
    private int _loaded = PageSize;

    [ObservableProperty]
    public partial bool HasMoreItems { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial bool IsGrouped { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCellCopyEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool HasRowMenu { get; set; } = true;

    // One menu for every row; each action gets the row's product. The grid adds "Copy <column>" on top.
    private readonly MenuItems _rowMenu;

    public MenuItems? RowMenu => HasRowMenu ? _rowMenu : null;

    public DataGridPageViewModel()
    {
        _rowMenu =
        [
            new MenuSection
            {
                new MenuAction("Details", "info.svg", RowTappedCommand),
                new MenuAction("Star", "star.svg", ToggleFavouriteCommand),
            },
            new MenuAction("Delete", "trashcan.svg", DeleteCommand) { IsDestructive = true },
        ];
    }

    partial void OnHasRowMenuChanged(bool value) => OnPropertyChanged(nameof(RowMenu));

    public bool ShowGrid => !ShowCode;

    [ObservableProperty]
    public partial string LastAction { get; set; } = "Tap a header to sort, or a row, a SKU or a star. Swipe a row; long-press a header, or a row for its menu.";

    public string? GroupByPath => IsGrouped ? nameof(Product.Category) : null;

    partial void OnIsGroupedChanged(bool value)
    {
        OnPropertyChanged(nameof(GroupByPath));

        // Grouping and load more do not combine (see the docs), so grouping shows everything.
        if (value)
            LoadUpTo(Catalogue.Count);
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        IsLoadingMore = true;
        await Task.Delay(800);
        LoadUpTo(_loaded + PageSize);
        IsLoadingMore = false;
    }

    private void LoadUpTo(int count)
    {
        foreach (var product in Catalogue.Take(count).Skip(_loaded))
            Products.Add(product);
        _loaded = Math.Max(_loaded, Math.Min(count, Catalogue.Count));
        HasMoreItems = _loaded < Catalogue.Count;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await Task.Delay(1000);
        Products.Clear();
        _loaded = 0;
        LoadUpTo(IsGrouped ? Catalogue.Count : PageSize);
        IsRefreshing = false;
        LastAction = "Refreshed";
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(ShowCode))
            OnPropertyChanged(nameof(ShowGrid));
    }

    [RelayCommand]
    private Task ShowGridOptions() => ShowOptionsAsync("DataGrid",
        new ToggleOption("Group by category", "Rows under a header per category. Grouping shows every row: it does not combine with load more.", () => IsGrouped, v => IsGrouped = v),
        new ToggleOption("Pull to refresh", "Pull the list down to load it again.", () => IsRefreshEnabled, v => IsRefreshEnabled = v),
        new ToggleOption("Row menu", "Long-press a row, or right-click it, for its context menu: copy the value you pressed, details, star or delete.", () => HasRowMenu, v => HasRowMenu = v),
        new ToggleOption("Copy on long-press", "Long-press a value to copy what it shows. With the row menu, Copy is the menu's first row.", () => IsCellCopyEnabled, v => IsCellCopyEnabled = v));

    [RelayCommand]
    private void RowTapped(Product product) => LastAction = $"Row: {product.Name}";

    [RelayCommand]
    private void OpenSku(Product product) => LastAction = $"SKU link: {product.Sku}";

    [RelayCommand]
    private void ToggleFavourite(Product product)
    {
        product.IsFavourite = !product.IsFavourite;
        LastAction = product.IsFavourite ? $"Starred {product.Name}" : $"Unstarred {product.Name}";
    }

    [RelayCommand]
    private void Delete(Product product)
    {
        Products.Remove(product);
        LastAction = $"Deleted {product.Name}";
    }
}

public partial class Product : ObservableObject
{
    public required string Sku { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required decimal Price { get; init; }
    public required int Stock { get; init; }
    public required DateTime Updated { get; init; }

    [ObservableProperty]
    public partial bool IsFavourite { get; set; }

    public bool IsLowStock => Stock < 10;

    private static readonly string[] Categories = ["Kitchen", "Garden", "Office", "Outdoor", "Lighting"];
    private static readonly string[] Adjectives = ["Compact", "Classic", "Folding", "Heavy-duty", "Minimal", "Rechargeable", "Stackable", "Weatherproof"];
    private static readonly string[] Nouns = ["lamp", "chair", "kettle", "planter", "desk", "lantern", "shelf", "hose", "stool", "grill", "tray", "clock"];

    public static List<Product> Generate(int count)
    {
        var random = new Random(335);
        var today = DateTime.Today;
        return [.. Enumerable.Range(0, count).Select(i => new Product
        {
            Sku = $"{random.Next(100, 999)}-{random.Next(10000, 99999)}",
            Name = $"{Adjectives[random.Next(Adjectives.Length)]} {Nouns[random.Next(Nouns.Length)]}",
            Category = Categories[random.Next(Categories.Length)],
            Price = Math.Round((decimal)(random.NextDouble() * 900 + 9), 2),
            Stock = random.Next(0, 250),
            Updated = today.AddDays(-random.Next(0, 400)),
            IsFavourite = random.Next(6) == 0,
        })];
    }
}
