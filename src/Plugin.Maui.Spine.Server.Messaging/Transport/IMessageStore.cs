namespace Plugin.Maui.Spine.Server.Messaging.Transport;

/// <summary>
/// The state messaging keeps beside the queue, for every transport: offloaded payloads, failed messages, deduplication
/// and cancel markers, and the janitor's lock. Implemented on blob storage by the Azure packages, and in memory.
/// </summary>
/// <remarks>Everything it holds expires, and <see cref="PurgeAsync"/> removes what has.</remarks>
public interface IMessageStore
{
    /// <summary>Creates the container or whatever the store needs.</summary>
    Task ProvisionAsync(CancellationToken cancellationToken);

    /// <summary>Keeps a payload until <paramref name="expiresAt"/>.</summary>
    Task<PayloadReference> PutPayloadAsync(string messageId, ReadOnlyMemory<byte> body, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Reads a payload back; <see langword="null"/> when it is gone.</summary>
    Task<ReadOnlyMemory<byte>?> GetPayloadAsync(PayloadReference payload, CancellationToken cancellationToken);

    /// <summary>Deletes a payload. A missing one is not an error.</summary>
    Task DeletePayloadAsync(PayloadReference payload, CancellationToken cancellationToken);

    /// <summary>
    /// Reserves a deduplication id until <paramref name="expiresAt"/>. <see langword="false"/> when it is already
    /// reserved and has not expired.
    /// </summary>
    Task<bool> TryReserveAsync(string deduplicationId, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Gives a deduplication id back, after a publish that reserved it failed. A missing one is not an error.</summary>
    Task ReleaseReservationAsync(string deduplicationId, CancellationToken cancellationToken);

    /// <summary>Marks a scheduled message as cancelled.</summary>
    Task CancelAsync(string messageId, DateTimeOffset deliverAt, CancellationToken cancellationToken);

    /// <summary>Whether a scheduled message was cancelled.</summary>
    Task<bool> IsCancelledAsync(string messageId, DateTimeOffset deliverAt, CancellationToken cancellationToken);

    /// <summary>Keeps a failed message, with its payload inline.</summary>
    Task SaveFailedAsync(FailedMessage message, CancellationToken cancellationToken);

    /// <summary>The failed messages, of one subscriber or of all.</summary>
    IAsyncEnumerable<FailedMessage> ListFailedAsync(string? subscriber, CancellationToken cancellationToken);

    /// <summary>Deletes a failed message. A missing one is not an error.</summary>
    Task DeleteFailedAsync(FailedMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a named lock held across instances; <see langword="null"/> when another instance holds it. Disposing
    /// releases it, and a holder that dies loses it within a minute.
    /// </summary>
    Task<IAsyncDisposable?> TryLockAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Removes what has expired: payloads and deduplication ids past their expiry, and failed messages and cancel
    /// markers older than <paramref name="retention"/>.
    /// </summary>
    /// <returns>How many items were removed.</returns>
    Task<int> PurgeAsync(DateTimeOffset now, TimeSpan retention, CancellationToken cancellationToken);
}
