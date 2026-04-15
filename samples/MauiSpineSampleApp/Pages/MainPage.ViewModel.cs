using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MauiSpineSampleApp.Pages;

public partial class MainPageViewModel(INavigationService _navigation) : ViewModelBase
{

    public double HeaderMinHeight => SystemBarInsets.Top + 32;

    public ObservableCollection<Item> Items { get; set; } = new ObservableCollection<Item>();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SystemBarInsets))
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMinHeight)));
    }

    [RelayCommand] private async Task OpenSettings() => await _navigation.NavigateToAsync<Settings.SettingsPage>();

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
            var items = Enumerable.Range(1, 30).Select(i => new Item { Title = $"Item {i}", IsMovable = false });

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
    private string? title;

    [ObservableProperty]
    private bool isMovable;
}