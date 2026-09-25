using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MauiSpineSampleApp.Pages;

public partial class MainPageViewModel(INavigationService _navigation) : ViewModelBase
{

    // The collapsed hero header keeps room for the gear, which sits where the header bar's
    // buttons sit on every other page: the status-bar inset down, 10 points in from the edge.
    // The collapsed hero: a bar below the status bar with the title and the gear centred on one line.
    private const double CompactBar = 36;

    public double HeaderMinHeight => SystemBarInsets.Top + CompactBar;

    // Tall enough that the photo's S starts below the status bar (and the Dynamic Island) rather than behind it.
    public double HeaderMaxHeight => SystemBarInsets.Top + 270;

    public Thickness GearMargin => DeviceInfo.Platform == DevicePlatform.WinUI
        ? new Thickness(0, 0, 144, 0)
        : new Thickness(0, SystemBarInsets.Top + (CompactBar - 44) / 2, 10 + SystemBarInsets.Right, 0);

    // The photo runs edge to edge; the title and the rows keep clear of the Dynamic Island and the
    // rounded corners in landscape.
    public Thickness TitleMargin => new(10 + SystemBarInsets.Left, -4, 10, -4);

    public Thickness ListMargin => new(SystemBarInsets.Left, 0, SystemBarInsets.Right, 0);

    public double FooterHeight => SystemBarInsets.Bottom;

    // Filled before the page appears, so the first frame already has the rows and their icons.
    public ObservableCollection<Item> Items { get; } = new(SampleIndex.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SystemBarInsets))
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMinHeight)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMaxHeight)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(GearMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(TitleMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ListMargin)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(FooterHeight)));
        }
    }

    [RelayCommand] private async Task OpenSettings() => await _navigation.NavigateToAsync<Settings.SettingsPage>();

    [RelayCommand]
    private async Task ItemTapped(Item item)
    {
        if (item.Open is { } open)
            await open(_navigation);
    }

    // One row per sample page. Add a page here when it gets a page of its own; the list shows them by title.
    private static IEnumerable<Item> SampleIndex =>
    [
        new("Bottom sheets", "Native sheets with detents, blur, full screen, page actions and a footer", "sheet.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Sheets.SheetsPage>()),
        new("Parameters and results", "Typed navigation parameters and awaited results", "return.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Results.ResultsPage>()),
        new("Page binding", "{PageCommand} and {PageBinding} reach the page's view model from a template", "link.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<PageBinding.PageBindingPage>()),
        new("Header bar", "Layout, large title, background, foreground and status bar: every combination live, with the code for it", "headerbar.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<HeaderBar.HeaderBarPage>()),
        new("Materials", "Material.Kind: glass, blur, tinted and solid surfaces for any Border, and glass that merges in a MaterialContainer", "layers.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Materials.MaterialsPage>()),
        new("Page lifetime", "Poll, WhileVisible and PageLifetime: work that runs, pauses and stops with the page", "refresh.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Lifetime.LifetimePage>()),
        new("Menu buttons", "A header action, a pop-up button and an icon button that open native menus: sections, pickers, submenus, toggles", "more.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<Menus.MenusPage>()),
        new("Page actions", "[PageAction] on a command; text, badge, enabled and visibility change live", "energy.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<PageActions.PageActionsPage>()),
        new("Liquid Glass", "Button and ImageButton as glass on iOS 26", "water.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg", n => n.NavigateToAsync<Glass.GlassPage>()),
        new("Scroll inset", "SafeArea.ScrollInset: a list that scrolls clear of the bottom bar it draws behind", "vertical.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<ScrollInset.ScrollInsetPage>()),
        new("Typography", "Text.FontFeatures (tabular digits) and Text.TrimToCapHeight", "edit.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Typography.TypographyPage>()),
        new("AnimatedLabel", "Marquee for text that does not fit, fade on change", "text.svg", "Plugin.Maui.Spine.Controls.AnimatedLabel", n => n.NavigateToAsync<Marquee.MarqueePage>()),
        new("Calendar", "Month calendar with swipe, year and decade pickers, week numbers, marked days, theme and culture", "calendar.svg", "Plugin.Maui.Spine.Controls.Calendar", n => n.NavigateToAsync<Dates.DatesPage>()),
        new("DataGrid", "Rows with named Wide/Narrow layouts, sorting, grouping, swipe actions, load more and pull-to-refresh", "grid.svg", "Plugin.Maui.Spine.Controls.DataGrid, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<DataGrid.DataGridPage>()),
        new("Rows", "SpineRow settings and key/value rows, Tap.Command with press feedback on any view, Semantic.Merge for one screen-reader element", "list.svg", "Plugin.Maui.Spine.Controls.Rows, Plugin.Maui.Spine", n => n.NavigateToAsync<Rows.RowsPage>()),
        new("Shimmer", "Skeleton loading: a Shimmer over placeholders, and Skeleton.IsActive on the real list and detail layouts", "lightstrip.svg", "Plugin.Maui.Spine.Controls.Shimmer", n => n.NavigateToAsync<Shimmer.ShimmerPage>()),
        new("Theming", "IThemeService: a stored light/dark choice, an app-wide accent, token dictionaries, a repaint hook for code-drawn views", "theme.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Theme.ThemePage>()),
        new("Strings", "ISpineStrings: embedded XML per culture, {String} with arguments and plurals, a runtime language switch", "globe.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Common", n => n.NavigateToAsync<Strings.StringsPage>()),
        new("SVG icons", "SvgImageSource on Image and ImageButton, the bundled icon set", "fish.svg", "Plugin.Maui.Spine.Svg, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<SvgIcons.SvgIconsPage>()),
    ];
}

public partial class Item : ObservableObject
{
    public Item() { }

    public Item(string title, string description, string icon, string packages, Func<INavigationService, Task> open)
    {
        Title = title;
        Description = description;
        Icon = icon;
        Packages = packages;
        Open = open;
    }

    public Func<INavigationService, Task>? Open { get; init; }

    /// <summary>Comma-separated NuGet ids the sample depends on.</summary>
    public string? Packages { get; init; }

    [ObservableProperty]
    public partial string? Icon { get; set; }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial bool IsMovable { get; set; }
}