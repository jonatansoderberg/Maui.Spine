using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Widgets.Hockey;

/// <summary>
/// A toggle in Control Center (iOS 18) and Quick Settings (Android): goal alerts on or off. The system flips
/// it at once; the tap reaches <see cref="OnActionAsync"/> in the app's process, which the platform starts in
/// the background when it is not running.
/// </summary>
[Control(Kind)]
public sealed class GoalAlertsControl(LiveScore _score) : IControlProvider
{
    public const string Kind = "goal-alerts";

    public Task<ControlState> GetStateAsync(ControlContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ControlState.Toggle("Goal alerts", _score.GoalAlerts, "bell") with { Tint = WidgetColor.Orange });

    public Task OnActionAsync(ControlAction action)
    {
        _score.GoalAlerts = action.IsOn == true;
        return Task.CompletedTask;
    }
}
