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
        new("Bottom sheets", "Native sheets with detents, blur, full screen, switches in a template", "up.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Sheets.SheetsPage>()),
        new("Parameters and results", "Typed navigation parameters and awaited results", "return.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Results.ResultsPage>()),
        new("Page binding", "{PageCommand} and {PageBinding} reach the page's view model from a template", "wired.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<PageBinding.PageBindingPage>()),
        new("Page actions", "[PageAction] on a command; text, badge, enabled and visibility change live", "more.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg.Icons", n => n.NavigateToAsync<PageActions.PageActionsPage>()),
        new("Liquid Glass", "Button and ImageButton as glass on iOS 26", "water.svg", "Plugin.Maui.Spine, Plugin.Maui.Spine.Svg", n => n.NavigateToAsync<Glass.GlassPage>()),
        new("Scroll inset", "SafeArea.ScrollInset: a list that scrolls clear of the bottom bar it draws behind", "vertical.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<ScrollInset.ScrollInsetPage>()),
        new("Typography", "Text.FontFeatures (tabular digits) and Text.TrimToCapHeight", "edit.svg", "Plugin.Maui.Spine", n => n.NavigateToAsync<Typography.TypographyPage>()),
        new("AnimatedLabel", "Marquee for text that does not fit, fade on change", "horizontal.svg", "Plugin.Maui.Spine.Controls.AnimatedLabel", n => n.NavigateToAsync<Marquee.MarqueePage>()),
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