using System.ComponentModel;

namespace MauiSpineSampleApp.Pages.ScrollInset;

public partial class ScrollInsetPageViewModel : ViewModelBase
{
    public IReadOnlyList<string> Rows { get; } = Enumerable.Range(1, 40).Select(i => $"Row {i}").ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Edges))]
    public partial bool IsInsetEnabled { get; set; } = true;

    public Plugin.Maui.Spine.Core.SafeAreaEdges Edges => IsInsetEnabled ? Plugin.Maui.Spine.Core.SafeAreaEdges.Bottom : Plugin.Maui.Spine.Core.SafeAreaEdges.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseCollectionView))]
    public partial bool UseScrollView { get; set; }

    public bool UseCollectionView => !UseScrollView;

    public double BottomInset => SafeAreaInsets.Bottom;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SafeAreaInsets))
            OnPropertyChanged(nameof(BottomInset));
    }
}
