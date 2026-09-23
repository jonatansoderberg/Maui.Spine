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

    public ObservableCollection<Item> Items { get; set; } = new ObservableCollection<Item>();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SystemBarInsets))
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMinHeight)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(GearMargin)));
        }
    }

    // The hero page hides the header bar and draws its own gear; the declared action still
    // exists so the Windows title bar (which reads PageActions) shows it.
    [PageAction(Svg = "settings.svg")]
    [RelayCommand] private async Task OpenSettings() => await _navigation.NavigateToAsync<Settings.SettingsPage>();

    [RelayCommand]
    private async Task ItemTapped(Item item)
    {
        switch (Items.IndexOf(item))
        {
            case 0: await _navigation.NavigateToAsync<MainPageOld>(); break;
            case 1: await _navigation.NavigateToAsync<Glass.GlassPage>(); break;
            case 2: await _navigation.NavigateToAsync<ScrollInset.ScrollInsetPage>(); break;
            case 3: await _navigation.NavigateToAsync<PageActions.PageActionsPage>(); break;
        }
    }

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (Items is [])
        {
            var items = Enumerable.Range(1, 30).Select(i => i switch
            {
                2 => new Item { Icon = "fish.svg", Title = "Liquid Glass", Description = "Button and ImageButton as glass on iOS 26", IsMovable = false },
                3 => new Item { Icon = "fish.svg", Title = "Scroll inset", Description = "SafeArea.ScrollInset: a list that scrolls clear of the bottom bar it draws behind", IsMovable = false },
                4 => new Item { Icon = "fish.svg", Title = "Page actions", Description = "[PageAction] on a command; text, badge, enabled and visibility change live", IsMovable = false },
                _ => new Item
                {
                    Icon = "fish.svg",
                    Title = $"Item {i}",
                    Description = i % 2 == 0 ? $"Description for item {i} with extra details that may scroll since it is a long description that does not fit" : null,
                    IsMovable = false
                }
            });

            foreach (var item in items)
                Items.Add(item);
        }

        return base.OnAppearingAsync(navigationDirection);
    }
}

[ObservableObject]
public partial class Item
{
    [ObservableProperty]
    private string? icon;

    [ObservableProperty]
    private string? title;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private bool isMovable;
}