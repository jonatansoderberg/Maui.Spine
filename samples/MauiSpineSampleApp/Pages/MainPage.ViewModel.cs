using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MauiSpineSampleApp.Pages;

public partial class MainPageViewModel(INavigationService _navigation) : ViewModelBase
{

    // The collapsed hero header keeps room for the gear, which sits where the header bar's
    // buttons sit on every other page: the status-bar inset down, 10 points in from the edge.
    public double HeaderMinHeight => SystemBarInsets.Top + 44;

    public Thickness GearMargin => DeviceInfo.Platform == DevicePlatform.WinUI
        ? new Thickness(0, 0, 144, 0)
        : new Thickness(0, SystemBarInsets.Top, 10, 0);

    public double FooterHeight => SystemBarInsets.Bottom;

    // Filled before the page appears, so the first frame already has the rows and their icons.
    public ObservableCollection<Item> Items { get; } = new(SampleIndex);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SystemBarInsets))
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMinHeight)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(GearMargin)));
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

    // One row per sample page. Add a page here when it gets a page of its own.
    private static IEnumerable<Item> SampleIndex =>
    [
        new("Bottom sheets", "Native sheets with detents, blur, full screen, page actions and a footer", "up.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Sheets.SheetsPage>()),
        new("Parameters and results", "Typed navigation parameters and awaited results", "return.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Results.ResultsPage>()),
        new("Page binding", "{PageCommand} and {PageBinding} reach the page's view model from a template", "wired.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<PageBinding.PageBindingPage>()),
        new("Collapsing header", "HeaderBarMode.CollapseOnScroll: a large title that scrolls away into the header bar", "windowblinds.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Collapsing.CollapsingPage>()),
        new("Overlay header", "HeaderBarMode.Overlay, HeaderBarForeground and StatusBarStyle over a photo", "cam.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Overlay.OverlayPage>()),
        new("Page lifetime", "Poll, WhileVisible and PageLifetime: work that runs, pauses and stops with the page", "refresh.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Lifetime.LifetimePage>()),
        new("Menu buttons", "A header action, a pop-up button and an icon button that open native menus: sections, pickers, submenus, toggles", "more.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<Menus.MenusPage>()),
        new("Page actions", "[PageAction] on a command; text, badge, enabled and visibility change live", "more.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<PageActions.PageActionsPage>()),
        new("Liquid Glass", "Button and ImageButton as glass on iOS 26", "water.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg", n => n.NavigateToAsync<Glass.GlassPage>()),
        new("Scroll inset", "SafeArea.ScrollInset: a list that scrolls clear of the bottom bar it draws behind", "vertical.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<ScrollInset.ScrollInsetPage>()),
        new("Typography", "Text.FontFeatures (tabular digits) and Text.TrimToCapHeight", "edit.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Typography.TypographyPage>()),
        new("AnimatedLabel", "Marquee for text that does not fit, fade on change", "horizontal.svg", "Plugin.Maui.Spine.Controls.AnimatedLabel", n => n.NavigateToAsync<Marquee.MarqueePage>()),
        new("Calendar", "Month calendar with swipe, year and decade pickers, week numbers, theme and culture", "calendarday.svg", "Plugin.Maui.Spine.Controls.Calendar", n => n.NavigateToAsync<Dates.DatesPage>()),
        new("DataGrid", "Rows with named Wide/Narrow layouts, sorting, grouping, swipe actions, load more and pull-to-refresh", "wordclock.svg", "Plugin.Maui.Spine.Controls.DataGrid, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<DataGrid.DataGridPage>()),
        new("Theming", "IThemeService: a stored light/dark choice, an app-wide accent, token dictionaries, a repaint hook for code-drawn views", "lamp.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Theme.ThemePage>()),
        new("Strings", "ISpineStrings: embedded XML per culture, {String} with arguments and plurals, a runtime language switch", "wordclock.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Common", n => n.NavigateToAsync<Strings.StringsPage>()),
        new("SVG icons", "SvgImageSource on Image and ImageButton, the bundled icon set", "fish.svg", "Plugin.Maui.Spine.Svg, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<SvgIcons.SvgIconsPage>()),
    ];
}

[ObservableObject]
public partial class Item
{
    public Item() { }

    public Item(string title, string description, string icon, string packages, Func<INavigationService, Task> open)
    {
        this.title = title;
        this.description = description;
        this.icon = icon;
        Packages = packages;
        Open = open;
    }

    public Func<INavigationService, Task>? Open { get; init; }

    /// <summary>Comma-separated NuGet ids the sample depends on.</summary>
    public string? Packages { get; init; }

    [ObservableProperty]
    private string? icon;

    [ObservableProperty]
    private string? title;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private bool isMovable;
}