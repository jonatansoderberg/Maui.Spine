namespace Orientera.Services.Notifications;

/// <summary>
/// The push registration, as the notification service needs it. Spine.Push's own
/// <c>IPushService</c> is a MAUI type; this seam is what lets the planning and the tags be
/// compiled and tested on plain .NET, the same reason <see cref="INotificationScheduler"/> exists.
/// </summary>
public interface IPushRegistration
{
    /// <summary>
    /// Whether the backend can actually reach this installation. False means nothing arrives as
    /// push, so everything the device can work out for itself is scheduled locally after all.
    /// </summary>
    bool IsRegistered { get; }

    /// <summary>Replaces the tags this installation is registered with.</summary>
    Task SetTagsAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default);
}

/// <summary>Where push is not set up. Never registered, so nothing is left out of the local plan.</summary>
public sealed class NoPushRegistration : IPushRegistration
{
    public bool IsRegistered => false;

    public Task SetTagsAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
