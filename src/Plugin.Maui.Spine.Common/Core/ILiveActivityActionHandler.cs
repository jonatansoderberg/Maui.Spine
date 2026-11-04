namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Handles a tapped <see cref="W.Button"/> in a Live Activity: on its Lock Screen, in the Dynamic Island's expanded
/// regions or in <c>Small</c> on iOS, and in its notification on Android. Register one in the app's services. It runs in
/// the app's process without opening the app (on iOS within the 30 seconds an App Intent gets), and the activity changes
/// only when the handler updates or ends it.
/// </summary>
public interface ILiveActivityActionHandler
{
    /// <summary>Handles <paramref name="action"/>.</summary>
    Task OnActionAsync(LiveActivityAction action);
}

/// <summary>A tapped button of the Live Activity <paramref name="Kind"/>.</summary>
/// <param name="Kind">The activity's kind, as it was started with.</param>
/// <param name="ActionId">The button's action.</param>
/// <param name="At">When it was tapped.</param>
public sealed record LiveActivityAction(string Kind, string ActionId, DateTimeOffset At);

/// <summary>Actions Spine handles itself in a Live Activity, with no handler of the app's.</summary>
public static class LiveActivityActions
{
    /// <summary>Ends the activity: <c>W.Button(LiveActivityActions.End, W.Label("Avsluta"))</c>.</summary>
    public const string End = "end";
}
