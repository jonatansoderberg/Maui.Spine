namespace MauiSpineSampleApp.Pages.Collapsing;

public partial class CollapsingPageViewModel : ViewModelBase
{
    public IReadOnlyList<string> Rows { get; } = Enumerable.Range(1, 40).Select(i => $"Row {i}").ToList();

    // HeaderBarCollapseProgress follows the scroll: 0 with the large title in view, 1 once the
    // bar's own title is in. Shown here only to make the number visible.
    public string ProgressText => $"HeaderBarCollapseProgress = {HeaderBarCollapseProgress:0.00}";

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(HeaderBarCollapseProgress))
            OnPropertyChanged(nameof(ProgressText));
    }
}
