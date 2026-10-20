namespace MauiSpineSampleApp.Pages.PageActions;

public partial class PageActionsPageViewModel : SampleViewModel
{
    private int _count;
    private PageAction? _cancel;

    public PageActionsPageViewModel()
    {
        // The header bar has one trailing slot, and it goes to the first visible Secondary action:
        // the base class's theme menu would take it from the actions this page is about.
        PageActions.Remove(PageActions.Single(a => a.Menu == ThemeMenu));
    }

    [ObservableProperty]
    public partial string Log { get; set; } = "Tap Filter at the top right.";

    [ObservableProperty]
    public partial string CancelLabel { get; set; } = "Show Cancel instead of Back";

    // Declared once by the attribute; nothing to add in OnAppearingAsync.
    [PageAction("Filter", Order = 0)]
    [RelayCommand]
    private void Filter() => Log = $"Filter tapped at {DateTime.Now:HH:mm:ss}";

    // Second in the Secondary slot: takes over when Filter hides itself. Busy while it rings, so its icon turns.
    [PageAction(Svg = "bell.svg", Order = 1, ShowsBusy = true)]
    [RelayCommand]
    private async Task Bell()
    {
        Log = "Ringing…";
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        Log = $"Bell rang at {DateTime.Now:HH:mm:ss}";
    }

    // Filter steps aside so the bell, and its turning icon, has the slot while it rings.
    [RelayCommand]
    private async Task Ring()
    {
        FilterAction.IsVisible = false;
        await BellCommand.ExecuteAsync(null);
        await Task.Delay(TimeSpan.FromSeconds(1));
        FilterAction.IsVisible = true;
    }

    private PageAction FilterAction => PageActions.First(a => a.Command == FilterCommand);

    [RelayCommand]
    private Task ShowFilterOptions() => ShowOptionsAsync("Filter",
        new ToggleOption("Enabled", "Off, the button greys out and takes no taps.", () => FilterAction.IsEnabled, value => FilterAction.IsEnabled = value),
        new ToggleOption("Visible", "Off, Filter leaves the header, and Bell, the next Secondary action, takes its place.", () => FilterAction.IsVisible, value => FilterAction.IsVisible = value),
        new ToggleOption("Busy", "On, the button dims and takes no taps; a text button does not turn.", () => FilterAction.IsBusy, value => FilterAction.IsBusy = value));

    [RelayCommand]
    private void CountUp()
    {
        _count++;
        FilterAction.Text = $"Filter ({_count})";
        FilterAction.Badge = _count.ToString();
    }

    [RelayCommand]
    private void ClearCount()
    {
        _count = 0;
        FilterAction.Text = "Filter";
        FilterAction.Badge = null;
    }

    // Adding and removing at runtime works too; a Primary action replaces the back button.
    [RelayCommand]
    private void ToggleCancel()
    {
        if (_cancel is null)
        {
            _cancel = new PageAction("Cancel", ToggleCancelCommand) { Placement = PageActionPlacement.Primary };
            PageActions.Add(_cancel);
            CancelLabel = "Show Back again";
        }
        else
        {
            PageActions.Remove(_cancel);
            _cancel = null;
            CancelLabel = "Show Cancel instead of Back";
        }
    }
}
