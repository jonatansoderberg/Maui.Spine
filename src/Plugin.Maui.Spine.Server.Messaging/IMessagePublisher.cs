namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// Puts messages on the queue. Inject it anywhere; it is a singleton. A message type must be registered with
/// <see cref="MessagingBuilder.Message{T}"/>, and goes to every subscriber that takes it.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>Publishes one message.</summary>
    /// <exception cref="InvalidOperationException">The type is not registered, or a metadata key is not allowed.</exception>
    Task<PublishResult> PublishAsync<T>(T message, PublishOptions? options = null, CancellationToken cancellationToken = default) where T : notnull;

    /// <summary>
    /// Publishes many messages with the same options, in as few calls to the transport as it allows. A batch cannot set
    /// <see cref="PublishOptions.MessageId"/> or <see cref="PublishOptions.DeduplicationId"/>.
    /// </summary>
    Task<IReadOnlyList<PublishResult>> PublishBatchAsync<T>(IEnumerable<T> messages, PublishOptions? options = null, CancellationToken cancellationToken = default) where T : notnull;

    /// <summary>
    /// Cancels a scheduled message: when it comes due, it is dropped instead of handled. Cancelling a message that is
    /// already handled does nothing.
    /// </summary>
    /// <param name="messageId">The message's id.</param>
    /// <param name="deliverAt">When it was due, from <see cref="PublishResult.DeliverAt"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task CancelAsync(string messageId, DateTimeOffset deliverAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a scheduled message from its publish result.</summary>
    /// <exception cref="ArgumentException">The message was not scheduled.</exception>
    Task CancelAsync(PublishResult scheduled, CancellationToken cancellationToken = default) =>
        scheduled.DeliverAt is { } deliverAt
            ? CancelAsync(scheduled.MessageId, deliverAt, cancellationToken)
            : throw new ArgumentException("Only a scheduled message can be cancelled; this one had no due time.", nameof(scheduled));
}
