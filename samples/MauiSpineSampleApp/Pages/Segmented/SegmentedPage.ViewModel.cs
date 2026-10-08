namespace MauiSpineSampleApp.Pages.Segmented;

public partial class SegmentedPageViewModel(INavigationService navigation) : SampleViewModel
{
    private static readonly string[] Ranges = ["Day", "Week", "Month"];
    private static readonly string[] Views = ["list", "grid", "map"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    public partial int Range { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewText))]
    public partial int View { get; set; }

    [ObservableProperty]
    public partial int Sport { get; set; }

    [ObservableProperty]
    public partial bool IsWinterOpen { get; set; }

    public string RangeText => $"Showing one {Ranges[Range].ToLowerInvariant()}";

    public string ViewText => $"Shown as a {Views[View]}";

    [RelayCommand]
    private void NextRange() => Range = (Range + 1) % Ranges.Length;

    [RelayCommand]
    private Task OpenTopTabs() => navigation.NavigateToAsync<TopTabsPage>();
}
