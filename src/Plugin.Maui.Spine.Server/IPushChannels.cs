using System.Net;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// APNs broadcast channels. A push to a channel reaches every Live Activity that follows it, however
/// many there are; send one with <see cref="IPushSender.BroadcastLiveActivityAsync"/>. Needs the
/// Broadcast capability on the App ID, and iOS 18 on the device.
/// </summary>
/// <remarks>
/// A channel belongs to one APNs environment: one made in the sandbox does not exist in production.
/// Each method takes the environment, or uses <see cref="ApplePushOptions.Environment"/> when that
/// names one.
/// </remarks>
public interface IPushChannels
{
    /// <summary>Creates a channel. APNs picks the id; an app has at most 10,000.</summary>
    /// <param name="storage">What APNs keeps for a device that was offline. Fixed for the channel's life.</param>
    /// <param name="environment">Which APNs environment; see the remarks on <see cref="IPushChannels"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The channel id, which the app starts its activity with and the server broadcasts to.</returns>
    /// <exception cref="PushChannelException">APNs refused.</exception>
    Task<string> CreateAsync(
        PushChannelStorage storage = PushChannelStorage.None,
        ApnsEnvironment? environment = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every channel the app has in the environment.</summary>
    /// <param name="environment">Which APNs environment; see the remarks on <see cref="IPushChannels"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The channel ids.</returns>
    /// <exception cref="PushChannelException">APNs refused.</exception>
    Task<IReadOnlyList<string>> ListAsync(ApnsEnvironment? environment = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes a channel for good. Its id cannot be made again, and APNs may still deliver what it stored.</summary>
    /// <param name="channel">The channel id.</param>
    /// <param name="environment">Which APNs environment; see the remarks on <see cref="IPushChannels"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="PushChannelException">APNs refused.</exception>
    Task DeleteAsync(string channel, ApnsEnvironment? environment = null, CancellationToken cancellationToken = default);
}

/// <summary>What APNs keeps for a device that was offline when a broadcast went out.</summary>
public enum PushChannelStorage
{
    /// <summary>Nothing. Allows a higher publishing budget; right for frequent updates, such as a score.</summary>
    None = 0,

    /// <summary>The most recent message, for at most eight hours. Right for infrequent updates.</summary>
    MostRecent = 1,
}

/// <summary>APNs refused a channel request.</summary>
/// <param name="statusCode">The HTTP status APNs answered.</param>
/// <param name="reason">APNs' own reason, when it gave one.</param>
/// <param name="message">What was asked and what came back.</param>
public sealed class PushChannelException(HttpStatusCode statusCode, string? reason, string message) : Exception(message)
{
    /// <summary>The HTTP status APNs answered.</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>APNs' own reason, such as <c>BadChannelId</c>, when it gave one.</summary>
    public string? Reason { get; } = reason;
}
