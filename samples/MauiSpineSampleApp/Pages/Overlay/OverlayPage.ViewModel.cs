namespace MauiSpineSampleApp.Pages.Overlay;

public partial class OverlayPageViewModel : ViewModelBase
{
    public IReadOnlyList<string> Rows { get; } = Enumerable.Range(1, 30).Select(i => $"Row {i}").ToList();

    [ObservableProperty]
    public partial string Log { get; set; } = "The bell is a page action on the overlay header.";

    public double PhotoHeight => 320;

    // SafeAreaInsets.Top is status bar plus header under an overlay header.
    public Thickness HeaderMargin => new(0, Math.Max(0, PhotoHeight - SafeAreaInsets.Top), 0, 0);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SafeAreaInsets))
            OnPropertyChanged(nameof(HeaderMargin));
    }

    // A glass action over the photo, in the header's foreground colour.
    [PageAction(Svg = "bell.svg")]
    [RelayCommand]
    private void Bell() => Log = $"Bell tapped at {DateTime.Now:HH:mm:ss}";
}
