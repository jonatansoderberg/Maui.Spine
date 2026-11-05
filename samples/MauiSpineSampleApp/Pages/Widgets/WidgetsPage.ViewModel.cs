using MauiSpineSampleApp.Widgets;
using MauiSpineSampleApp.Widgets.Hockey;
using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Pages.Widgets;

public partial class WidgetsPageViewModel : SampleViewModel
{
    private readonly IWidgetService _widgets;

    public WidgetsPageViewModel(IWidgetService widgets, LiveScore score)
    {
        _widgets = widgets;
        Score = score;

        Poll(TimeSpan.FromSeconds(1), _ => Score.TickAsync());
    }

    /// <summary>The same game as on the Live Activities page: a goal here changes both.</summary>
    public LiveScore Score { get; }

    [ObservableProperty]
    public partial string Refreshed { get; set; } = "Not refreshed yet";

    [ObservableProperty]
    public partial string ServerRefreshed { get; set; } = "";

    [ObservableProperty]
    public partial string PictureRefreshed { get; set; } = "";

    /// <summary>Asks the platform to reload the remote widget, which fetches from the server again.</summary>
    [RelayCommand]
    private async Task RefreshRemote()
    {
        await _widgets.RefreshAsync(RemoteWidget.Kind);
        ServerRefreshed = $"Asked to reload at {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private async Task RefreshPicture()
    {
        await _widgets.RefreshAsync(PictureWidget.Kind);
        PictureRefreshed = $"Built again at {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private async Task RefreshWidgets()
    {
        await _widgets.RefreshAllAsync();
        Refreshed = $"Refreshed at {DateTime.Now:HH:mm:ss}";
    }
}
