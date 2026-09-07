using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// Sends push through whichever services the targeted installations are registered with. One call
/// reaches every platform; Spine builds the right payload for each.
/// </summary>
public interface IPushSender
{
    /// <summary>Sends a user-visible notification.</summary>
    /// <param name="target">Who to reach.</param>
    /// <param name="notification">What to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>One delivery per installation the target matched.</returns>
    Task<PushResult> SendAsync(PushTarget target, PushNotification notification, CancellationToken cancellationToken = default);

    /// <summary>Wakes the app without showing anything, and hands <paramref name="data"/> to its handler.</summary>
    /// <param name="target">Who to reach.</param>
    /// <param name="data">What the handler should receive.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>One delivery per installation the target matched.</returns>
    Task<PushResult> SendSilentAsync(PushTarget target, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken = default);

    /// <summary>Starts a Live Activity that is not running yet.</summary>
    /// <param name="target">Who to reach.</param>
    /// <param name="kind">The kind to start it with; the app finds a running activity again by this.</param>
    /// <param name="layout">The trees to render.</param>
    /// <param name="alert">The text shown as the activity appears.</param>
    /// <param name="options">Timing and priority; defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>One delivery per installation the target matched.</returns>
    Task<PushResult> StartLiveActivityAsync(PushTarget target, string kind, LiveActivityLayout layout, PushAlert alert, LiveActivityOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Replaces the content of a running Live Activity, or ends it.</summary>
    /// <param name="target">Who to reach.</param>
    /// <param name="kind">The kind the activity was started with.</param>
    /// <param name="layout">The trees to render.</param>
    /// <param name="event">Update by default; <see cref="LiveActivityEvent.End"/> to finish it.</param>
    /// <param name="options">Timing and priority; defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>One delivery per installation the target matched.</returns>
    Task<PushResult> UpdateLiveActivityAsync(PushTarget target, string kind, LiveActivityLayout layout, LiveActivityEvent @event = LiveActivityEvent.Update, LiveActivityOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Asks the app to rebuild its widgets.</summary>
    /// <param name="target">Who to reach.</param>
    /// <param name="kind">One widget kind, or <see langword="null"/> for all of them.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>One delivery per installation the target matched.</returns>
    Task<PushResult> RefreshWidgetsAsync(PushTarget target, string? kind = null, CancellationToken cancellationToken = default);
}
