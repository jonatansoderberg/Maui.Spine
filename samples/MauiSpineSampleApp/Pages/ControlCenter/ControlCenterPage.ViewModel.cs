using MauiSpineSampleApp.Widgets.Hockey;
using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Pages.ControlCenter;

public partial class ControlCenterPageViewModel : SampleViewModel
{
    private readonly IControlService _controls;

    public ControlCenterPageViewModel(IControlService controls, LiveScore score)
    {
        _controls = controls;
        Score = score;

        Poll(TimeSpan.FromSeconds(1), _ => Score.TickAsync());
    }

    /// <summary>The live-score demo's game: a goal from Control Center changes it, and the widget and the Live Activity with it.</summary>
    public LiveScore Score { get; }

    public bool CanRequestAdd => _controls.IsSupported && DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.Version.Major >= 13;

    public string Support => !_controls.IsSupported
        ? "Controls need iOS 18 or Android 7; this device has none."
        : DeviceInfo.Platform == DevicePlatform.Android
            ? "Swipe down twice, tap the pencil, and drag Goal alerts and Goal Owls into the tiles. On Android 13 and later the buttons below ask for you."
            : "Swipe down from the top right, tap +, then Add a Control, and search for Spine Showcase. The Lock Screen and the Action button take the same controls.";

    [ObservableProperty]
    public partial string Added { get; set; } = "";

    [RelayCommand]
    private async Task AddAlerts() => Added = await _controls.RequestAddAsync(GoalAlertsControl.Kind) ? "Goal alerts is in Quick Settings." : "Goal alerts was not added.";

    [RelayCommand]
    private async Task AddGoal() => Added = await _controls.RequestAddAsync(GoalControl.Kind) ? "Goal Owls is in Quick Settings." : "Goal Owls was not added.";
}
