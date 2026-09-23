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

    public ObservableCollection<Item> Items { get; set; } = new ObservableCollection<Item>();

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
        new("Sheets, results and marquee", "Bottom sheets with detents, typed results, AnimatedLabel", n => n.NavigateToAsync<MainPageOld>()),
        new("Liquid Glass", "Button and ImageButton as glass on iOS 26", n => n.NavigateToAsync<Glass.GlassPage>()),
        new("Page binding", "{PageCommand} and {PageBinding} reach the page's view model from a template", n => n.NavigateToAsync<PageBinding.PageBindingPage>()),
    ];

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
        {
            //This is just creating a placeholder in the native title bar (Hack to make the header settings button clickable)
            PageActions.Add(new PageAction(text: null, command: OpenSettingsCommand)
            {
                Svg = "settings.svg"
            });
        }

        if (Items is [])
        {
            foreach (var item in SampleIndex)
                Items.Add(item);
        }

        return base.OnAppearingAsync(navigationDirection);
    }
}

[ObservableObject]
public partial class Item
{
    public Item() { }

    public Item(string title, string description, Func<INavigationService, Task> open)
    {
        this.title = title;
        this.description = description;
        this.icon = "fish.svg";
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