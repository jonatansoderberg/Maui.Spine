namespace MauiSpineSampleApp.Pages.PageActions;

public partial class PageActionsPageViewModel : ViewModelBase
{
    private int _count;
    private PageAction? _cancel;

    [ObservableProperty]
    public partial string Log { get; set; } = "The header shows the first visible Secondary action. Tap it, or change it below.";

    // Declared once by the attribute; nothing to add in OnAppearingAsync.
    [PageAction("Filter", Order = 0)]
    [RelayCommand]
    private void Filter() => Log = $"Filter tapped at {DateTime.Now:HH:mm:ss}";

    // Second in the Secondary slot: takes over when Filter hides itself.
    [PageAction(Svg = "bell.svg", Order = 1)]
    [RelayCommand]
    private void Bell() => Log = $"Bell tapped at {DateTime.Now:HH:mm:ss}";

    private PageAction FilterAction => PageActions.First(a => a.Command == FilterCommand);

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
    private void ToggleEnabled() => FilterAction.IsEnabled = !FilterAction.IsEnabled;

    [RelayCommand]
    private void ToggleVisible() => FilterAction.IsVisible = !FilterAction.IsVisible;

    // Adding and removing at runtime works too; a Primary action replaces the back button.
    [RelayCommand]
    private void ToggleCancel()
    {
        if (_cancel is null)
        {
            _cancel = new PageAction("Cancel", ToggleCancelCommand) { Placement = PageActionPlacement.Primary };
            PageActions.Add(_cancel);
        }
        else
        {
            PageActions.Remove(_cancel);
            _cancel = null;
        }
    }
}
