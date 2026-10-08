using MauiSpineSampleApp.Pages;

namespace MauiSpineSampleApp.Controls;

/// <summary>
/// The Example / Code switch at the top of a sample page, bound to <see cref="SampleViewModel.ShowCode"/>:
/// the page shows its examples, or the code for the same examples.
/// </summary>
public sealed class ExampleCodeSwitch : ContentView
{
    private readonly SegmentedControl _segments = new()
    {
        Segments = { new Segment { Title = "Example" }, new Segment { Title = "Code" } },
    };

    private SampleViewModel? _page;

    public ExampleCodeSwitch()
    {
        _segments.SelectionChanged += (_, e) =>
        {
            if (_page is not null)
                _page.ShowCode = e.NewIndex == 1;
        };
        Content = _segments;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_page is not null)
            _page.PropertyChanged -= OnPagePropertyChanged;

        _page = BindingContext as SampleViewModel;

        if (_page is not null)
            _page.PropertyChanged += OnPagePropertyChanged;

        Show();
    }

    private void OnPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SampleViewModel.ShowCode))
            Show();
    }

    private void Show() => _segments.SelectedIndex = _page?.ShowCode == true ? 1 : 0;
}
