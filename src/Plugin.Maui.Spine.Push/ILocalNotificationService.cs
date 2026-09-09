namespace Plugin.Maui.Spine.Push;

/// <summary>One notification this device shows on its own, at a time it already knows.</summary>
public sealed record LocalNotification
{
    /// <summary>
    /// Derived from what the notification is about rather than generated, so re-planning replaces a
    /// notification instead of stacking a second copy of it.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>When it should arrive. An instant that has passed is dropped, not fired late.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>The first line.</summary>
    public required string Title { get; init; }

    /// <summary>The body text.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// The page to open, the same <c>spine.route</c> a push carries — so the app's
    /// <see cref="IPushHandler.OnOpenedAsync"/> navigates the same way whichever half sent it.
    /// </summary>
    public string? Route { get; init; }

    /// <summary>
    /// The channel from <see cref="SpinePushOptions.AddChannel"/> to post it in, and the thread id on
    /// Apple. The app's first channel when <see langword="null"/>.
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>Anything else the handler should see when the notification is opened.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }
}

/// <summary>
/// The notifications this device shows without a server: a plan the app rebuilds from data it has
/// anyway, and hands over whole.
/// </summary>
/// <remarks>
/// Permission, channels and delivery are shared with push. The user is asked once, through
/// <see cref="IPushService.RequestPermissionAsync"/>; the channels are the ones
/// <see cref="SpinePushOptions.AddChannel"/> created; and an opened notification reaches the app's
/// <see cref="IPushHandler"/> with <see cref="PushMessage.IsLocal"/> set, so a handler that does not
/// care about the difference does not have to look.
/// <para>
/// What to notify about, and when, stays with the app. So does the choice of what not to schedule
/// because the backend already pushes it — <see cref="IPushService.IsRegistered"/> is the signal for
/// that, and the app is the only one who knows which of its own kinds it covers.
/// </para>
/// </remarks>
public interface ILocalNotificationService
{
    /// <summary>False where the platform has no scheduling to offer, so the app can say so rather than lie.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Makes the device's pending notifications equal <paramref name="plan"/>: anything not in it is
    /// cancelled. Re-planning is therefore idempotent, and something that moved or stopped being true
    /// stops notifying rather than firing from a stale schedule.
    /// </summary>
    /// <param name="plan">Every notification that should still arrive.</param>
    /// <param name="cancellationToken">Cancels the scheduling.</param>
    Task SyncAsync(IEnumerable<LocalNotification> plan, CancellationToken cancellationToken = default);

    /// <summary>What is still to come, as the device has it.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The pending notifications, earliest first.</returns>
    Task<IReadOnlyList<LocalNotification>> PendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancels everything this app has scheduled. Notifications already shown are left alone.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task CancelAllAsync(CancellationToken cancellationToken = default);
}
