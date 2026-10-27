namespace MauiSpineSampleApp.Pages.PageActions;

public partial class PageActionsPageViewModel : SampleViewModel
{
    private static readonly Color Orange = Color.FromArgb("#FF9500");
    private static readonly Color DarkOrange = Color.FromArgb("#FF9F0A");

    private int _count;
    private PageAction? _cancel;
    private readonly PageAction _star;

    public PageActionsPageViewModel()
    {
        // The header bar has one trailing slot, and it goes to the first visible Secondary action:
        // the base class's theme menu would take it from the actions this page is about.
        PageActions.Remove(PageActions.Single(a => a.Menu == ThemeMenu));

        // Added before the declared actions, so it sits at the trailing edge while it shows.
        _star = new PageAction(null, ToggleStarCommand)
        {
            Svg = "star.svg",
            Description = "Favourite",
            IsVisible = false,
            SelectedStyle = PageActionSelectedStyle.Tinted,
            Tint = Orange,
            DarkTint = DarkOrange,
        };
        PageActions.Add(_star);
    }

    [ObservableProperty]
    public partial string Log { get; set; } = "Tap Filter at the top right.";

    [ObservableProperty]
    public partial string CancelLabel { get; set; } = "Show Cancel instead of Back";

    [ObservableProperty]
    public partial string StarLabel { get; set; } = "Show the star";

    // Declared once by the attribute; nothing to add in OnAppearingAsync.
    [PageAction("Filter", Order = 0)]
    [RelayCommand]
    private void Filter() => Log = $"Filter tapped at {DateTime.Now:HH:mm:ss}";

    // Second in the Secondary slot: takes over when Filter hides itself. Busy while it rings, and its icon turns.
    [PageAction(Svg = "bell.svg", Order = 1, ShowsBusy = true, SpinsWhenBusy = true)]
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
        new ToggleOption("Busy", "On, the button dims and takes no taps, without turning.", () => FilterAction.IsBusy, value => FilterAction.IsBusy = value));

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

    [RelayCommand]
    private void ToggleStar() => _star.IsSelected = !_star.IsSelected;

    [RelayCommand]
    private void ShowStar()
    {
        _star.IsVisible = !_star.IsVisible;
        StarLabel = _star.IsVisible ? "Hide the star" : "Show the star";
    }

    [RelayCommand]
    private Task ShowStarOptions() => ShowOptionsAsync("Star",
        new ToggleOption("Selected", "On, the toggle the star stands for is on. Tapping the star does the same.", () => _star.IsSelected, value => _star.IsSelected = value),
        new ToggleOption("Tinted", "On, a selected star is orange on a faint orange wash. Off, it is filled, with its tint or the bar's foreground.", () => _star.SelectedStyle == PageActionSelectedStyle.Tinted,
            value => _star.SelectedStyle = value ? PageActionSelectedStyle.Tinted : PageActionSelectedStyle.Filled),
        new ToggleOption("Orange", "Off, the star has no colour of its own: it follows the bar, and Tinted uses the accent.", () => _star.Tint is not null, value =>
        {
            _star.Tint = value ? Orange : null;
            _star.DarkTint = value ? DarkOrange : null;
        }));

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
