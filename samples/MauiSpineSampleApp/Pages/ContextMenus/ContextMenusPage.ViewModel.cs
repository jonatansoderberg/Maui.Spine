using System.Collections.ObjectModel;

namespace MauiSpineSampleApp.Pages.ContextMenus;

public partial class ContextMenusPageViewModel : SampleViewModel
{
    [ObservableProperty]
    public partial string CardStatus { get; set; } = "Not followed";

    [ObservableProperty]
    public partial string RowStatus { get; set; } = "";

    private readonly MenuAction _follow;
    private readonly MenuAction _unfollow;

    // The card's menu, and the header's: one instance in both places.
    public MenuItems CardMenu { get; }

    // One menu for every row; ContextMenu.CommandParameter hands each command its row.
    public MenuItems RowMenu { get; }

    public ObservableCollection<Race> Races { get; } =
    [
        new("Night sprint", "Uppsala · Friday 18:30"),
        new("Long distance", "Sala · Saturday 10:00"),
        new("Relay", "Gävle · Sunday 09:30"),
        new("Middle distance", "Enköping · 14 Oct"),
        new("Club championship", "Uppsala · 21 Oct"),
    ];

    public ContextMenusPageViewModel()
    {
        _follow = new MenuAction("Follow", "star.svg", FollowCommand);
        _unfollow = new MenuAction("Unfollow", "star.svg", UnfollowCommand) { IsVisible = false };

        CardMenu =
        [
            new MenuSection
            {
                _follow,
                _unfollow,
                new MenuAction("Add to calendar", "calendar.svg", AddToCalendarCommand),
            },
            new MenuAction("Share", "share.svg", ShareCardCommand),
        ];

        RowMenu =
        [
            new MenuSection
            {
                new MenuAction("Copy name", "copy.svg", CopyCommand),
                new MenuAction("Share", "share.svg", ShareRowCommand),
            },
            new MenuAction("Remove", "trashcan.svg", RemoveCommand) { IsDestructive = true },
        ];

        // The header bar's one trailing slot shows the card's menu instead of the theme menu.
        PageActions.Remove(PageActions.Single(a => a.Menu == ThemeMenu));
        PageActions.Add(new PageAction(null, CardMenu) { Svg = "more.svg", Description = "Race actions" });
    }

    [RelayCommand]
    private void Open() => CardStatus = "Opened the night sprint";

    [RelayCommand]
    private void Follow() => SetFollowing(true);

    [RelayCommand]
    private void Unfollow() => SetFollowing(false);

    [RelayCommand]
    private void AddToCalendar() => CardStatus = "Added to the calendar";

    [RelayCommand]
    private void ShareCard() => CardStatus = "Shared the night sprint";

    [RelayCommand]
    private async Task Copy(Race race)
    {
        await Clipboard.Default.SetTextAsync(race.Name);
        RowStatus = $"Copied “{race.Name}”";
    }

    [RelayCommand]
    private void ShareRow(Race race) => RowStatus = $"Shared {race.Name.ToLowerInvariant()}";

    [RelayCommand]
    private void Remove(Race race)
    {
        Races.Remove(race);
        RowStatus = $"Removed {race.Name.ToLowerInvariant()}";
    }

    // Hidden, not dimmed: a context menu shows only what applies.
    private void SetFollowing(bool following)
    {
        _follow.IsVisible = !following;
        _unfollow.IsVisible = following;
        CardStatus = following ? "Following" : "Not followed";
    }
}

public sealed record Race(string Name, string Details);
