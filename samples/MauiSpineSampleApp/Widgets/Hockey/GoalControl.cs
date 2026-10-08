using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Widgets.Hockey;

/// <summary>
/// A button in Control Center and Quick Settings: a goal for the followed club in the running game. The
/// score under the title, the widget and the Live Activity all follow, and the app never comes to the front.
/// </summary>
[Control(Kind)]
public sealed class GoalControl(LiveScore _score) : IControlProvider
{
    public const string Kind = "goal";

    public Task<ControlState> GetStateAsync(ControlContext context, CancellationToken cancellationToken)
    {
        var game = _score.Game;
        var status = game.Phase switch
        {
            GamePhase.Live => $"{game.Home.Abbreviation} {game.Score} {game.Away.Abbreviation}",
            GamePhase.Scheduled => $"Face-off {game.FaceOffAt.ToLocalTime():HH:mm}",
            _ => "No game on",
        };

        // The SVG "plus" draws the Android tile; iOS draws only SF Symbols, so it gets a puck.
        return Task.FromResult(ControlState.Button($"Goal {Teams.Followed.ShortName}", "plus", status) with { Symbol = "hockey.puck" });
    }

    public Task OnActionAsync(ControlAction action) => _score.GoalForFollowedAsync();
}
