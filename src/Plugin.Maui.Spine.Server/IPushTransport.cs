using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// One push service. Everything above this seam is platform-neutral, so a hosted service such as
/// Azure Notification Hubs or OneSignal can be added as another implementation without the app or
/// the calling code noticing.
/// </summary>
public interface IPushTransport
{
    /// <summary>The platform this transport delivers to.</summary>
    PushPlatform Platform { get; }

    /// <summary>Delivers one already-built message to a batch of installations.</summary>
    /// <param name="installations">The registrations to reach; all of <see cref="Platform"/>.</param>
    /// <param name="message">What to send, in the shape this transport's platform expects.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>One delivery per installation, in no particular order.</returns>
    Task<IReadOnlyList<PushDelivery>> SendAsync(
        IReadOnlyList<PushInstallation> installations,
        PushEnvelope message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A message built for one platform: the body plus whatever the service needs beside it. Each
/// transport reads the properties its service understands and ignores the rest.
/// </summary>
public sealed record PushEnvelope
{
    /// <summary>The serialized body — the APNs payload, or the FCM message's data and notification.</summary>
    public required string Json { get; init; }

    /// <summary>The APNs <c>apns-push-type</c>: <c>alert</c>, <c>background</c>, <c>liveactivity</c> or <c>widgets</c>.</summary>
    public string? ApnsPushType { get; init; }

    /// <summary>The APNs topic, which is the bundle id with a suffix for some push types.</summary>
    public string? ApnsTopic { get; init; }

    /// <summary>Delivery priority, already translated to the service's numbering.</summary>
    public int Priority { get; init; }

    /// <summary>Replaces any undelivered message with the same id.</summary>
    public string? CollapseId { get; init; }

    /// <summary>When the service should give up.</summary>
    public DateTimeOffset? Expiration { get; init; }
}
