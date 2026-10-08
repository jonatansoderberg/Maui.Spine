using System.Collections.ObjectModel;

namespace MauiSpineSampleApp.Pages.ActionSheets;

public partial class ActionSheetsPageViewModel(INavigationService _navigation) : SampleViewModel
{
    [ObservableProperty]
    public partial string RaceStatus { get; set; } = "";

    [ObservableProperty]
    public partial string ResultsStatus { get; set; } = "12 results";

    [ObservableProperty]
    public partial string RowStatus { get; set; } = "";

    public ObservableCollection<Runner> Runners { get; } =
    [
        new("Anna Lindqvist", "H21 · 34:12"),
        new("Erik Berg", "H21 · 36:48"),
        new("Sara Holm", "D21 · 38:05"),
    ];

    [RelayCommand]
    private async Task ShowRaceActions()
    {
        var picked = await _navigation.ShowActionsAsync(new ActionSheet("Night sprint", "Uppsala · Friday 18:30")
        {
            Actions =
            [
                new("Share", "share.svg", ShareCommand),
                new("Add to calendar", "calendar.svg", AddToCalendarCommand),
                new("Copy link", "link.svg", CopyLinkCommand),
                new("Remove", "trashcan.svg", RemoveRaceCommand) { IsDestructive = true },
            ],
        });

        if (picked is null)
            RaceStatus = "Cancelled";
    }

    [RelayCommand]
    private void Share() => RaceStatus = "Shared the night sprint";

    [RelayCommand]
    private void AddToCalendar() => RaceStatus = "Added to the calendar";

    [RelayCommand]
    private void CopyLink() => RaceStatus = "Copied the link";

    [RelayCommand]
    private void RemoveRace() => RaceStatus = "Removed the night sprint";

    [RelayCommand]
    private async Task DeleteResults()
    {
        var delete = new MenuAction("Delete 12 results", "trashcan.svg") { IsDestructive = true };

        var picked = await _navigation.ShowActionsAsync(new ActionSheet(null, "The results of the night sprint are deleted from this phone. This can't be undone.")
        {
            Actions = [delete],
        });

        ResultsStatus = picked == delete ? "Deleted" : "Kept 12 results";
    }

    // The same rows for every runner; the sheet's CommandParameter hands each command its runner.
    private MenuAction[] RunnerActions =>
    [
        new("Message", "chat.svg", MessageCommand),
        new("Approve", "check.svg", ApproveCommand),
        new("Disqualify", "flag.svg", DisqualifyCommand) { IsDestructive = true },
    ];

    [RelayCommand]
    private Task ShowRunnerActions(View button)
    {
        var runner = (Runner)button.BindingContext;
        return _navigation.ShowActionsAsync(new ActionSheet(runner.Name)
        {
            Actions = RunnerActions,
            CommandParameter = runner,
        }, anchor: button);
    }

    [RelayCommand]
    private void Message(Runner runner) => RowStatus = $"Wrote to {runner.Name}";

    [RelayCommand]
    private void Approve(Runner runner) => RowStatus = $"Approved {runner.Name}";

    [RelayCommand]
    private void Disqualify(Runner runner)
    {
        Runners.Remove(runner);
        RowStatus = $"Disqualified {runner.Name}";
    }
}

public sealed record Runner(string Name, string Details);
