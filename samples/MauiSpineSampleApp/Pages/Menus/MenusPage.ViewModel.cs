namespace MauiSpineSampleApp.Pages.Menus;

public partial class MenusPageViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string LastPick { get; set; } = "Nothing picked yet";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    // The header action: sections, a picker, a submenu with toggles, a destructive row.
    private readonly MenuPicker _filter;
    private readonly MenuAction _photos = new("Photos", "cam.svg") { IsChecked = true, KeepsMenuOpen = true };
    private readonly MenuAction _videos = new("Videos", "play.svg") { KeepsMenuOpen = true };

    // A pop-up button: the button text follows the picked row.
    public MenuItems SortMenu { get; }

    // An icon button with plain actions.
    public MenuItems MoreMenu { get; }

    public MenusPageViewModel()
    {
        _filter = new MenuPicker(PickCommand)
        {
            new MenuAction("All items", "house.svg") { IsChecked = true },
            new MenuAction("Favourites", "star.svg"),
            new MenuAction("Edited", "edit.svg"),
        };

        PageActions.Add(new PageAction(null, new MenuItems
        {
            new MenuSection("Filter:") { _filter },
            new MenuSection
            {
                new SubMenu("Media types", "image.svg") { _photos, _videos },
                new SubMenu("View options")
                {
                    new MenuAction("Sort by date", "clock.svg", PickCommand),
                    new MenuAction("Reset", "refresh.svg", PickCommand) { IsDestructive = true },
                },
            },
        }) { Svg = "more.svg" });

        SortMenu =
        [
            new MenuPicker(PickCommand)
            {
                new MenuAction("Name") { IsChecked = true },
                new MenuAction("Date"),
                new MenuAction("Size") { IsEnabled = false },
            },
        ];

        MoreMenu =
        [
            new MenuAction("Refresh", "refresh.svg", PickCommand),
            new MenuAction("Settings", "settings.svg", PickCommand),
            new MenuSection { new MenuAction("Delete", "delete.svg", PickCommand) { IsDestructive = true } },
        ];

        _photos.PropertyChanged += (_, _) => Summarize();
        _videos.PropertyChanged += (_, _) => Summarize();
        Summarize();
    }

    [RelayCommand]
    private void Pick(object? parameter)
    {
        LastPick = parameter is MenuAction action ? $"Picked: {action.Title}" : $"Picked: {parameter}";
        Summarize();
    }

    private void Summarize() =>
        Summary = $"Filter: {_filter.Selected?.Title}; media: {string.Join(", ", new[] { _photos, _videos }.Where(a => a.IsChecked).Select(a => a.Title))}";
}
