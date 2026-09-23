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
        new("Bottom sheets", "Native sheets with detents, blur, full screen, switches in a template", "up.svg", n => n.NavigateToAsync<Sheets.SheetsPage>()),
        new("Parameters and results", "Typed navigation parameters and awaited results", "return.svg", n => n.NavigateToAsync<Results.ResultsPage>()),
        new("Page binding", "{PageCommand} and {PageBinding} reach the page's view model from a template", "wired.svg", n => n.NavigateToAsync<PageBinding.PageBindingPage>()),
        new("Liquid Glass", "Button and ImageButton as glass on iOS 26", "water.svg", n => n.NavigateToAsync<Glass.GlassPage>()),
        new("AnimatedLabel", "Marquee for text that does not fit, fade on change", "horizontal.svg", n => n.NavigateToAsync<Marquee.MarqueePage>()),
        new("SVG icons", "SvgImageSource on Image and ImageButton, the bundled icon set", "fish.svg", n => n.NavigateToAsync<SvgIcons.SvgIconsPage>()),
    ];
}

[ObservableObject]
public partial class Item
{
    public Item() { }

    public Item(string title, string description, string icon, Func<INavigationService, Task> open)
    {
        this.title = title;
        this.description = description;
        this.icon = icon;
        Open = open;
    }

    public Func<INavigationService, Task>? Open { get; init; }

    [ObservableProperty]
    private string? icon;

    [ObservableProperty]
    private string? title;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private bool isMovable;
}