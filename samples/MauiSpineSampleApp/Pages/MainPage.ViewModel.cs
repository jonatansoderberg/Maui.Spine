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
        switch (Items.IndexOf(item))
        {
            case 0: await _navigation.NavigateToAsync<MainPageOld>(); break;
            case 1: await _navigation.NavigateToAsync<Glass.GlassPage>(); break;
        }
    }

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
            var items = Enumerable.Range(1, 30).Select(i => i == 2
                ? new Item { Icon = "fish.svg", Title = "Liquid Glass", Description = "Button and ImageButton as glass on iOS 26", IsMovable = false }
                : new Item
                {
                    Icon = "fish.svg",
                    Title = $"Item {i}",
                    Description = i % 2 == 0 ? $"Description for item {i} with extra details that may scroll since it is a long description that does not fit" : null,
                    IsMovable = false
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