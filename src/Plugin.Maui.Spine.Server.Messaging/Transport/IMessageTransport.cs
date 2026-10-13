namespace Plugin.Maui.Spine.Server.Messaging.Transport;

/// <summary>
/// What a queue technology implements to carry Spine's messages: Azure Storage Queues, Azure Service Bus, or memory. App
/// code never calls it; <see cref="IMessagePublisher"/>, <see cref="MessageDispatcher"/> and the pump do.
/// </summary>
public interface IMessageTransport
{
    /// <summary>What the transport can carry.</summary>
    TransportLimits Limits { get; }

    /// <summary>Creates the queues, topics or subscriptions the topology needs, and brings their rules up to date.</summary>
    Task ProvisionAsync(MessageTopology topology, CancellationToken cancellationToken);

    /// <summary>
    /// Sends messages. A message not yet due is sent dormant, for at most <see cref="TransportLimits.MaxDelay"/>; the
    /// dispatcher sends it on when it surfaces early.
    /// </summary>
    Task SendAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken);

    /// <summary>Takes up to <paramref name="maxMessages"/> messages from a lane, or none if it is empty.</summary>
    /// <param name="lane">The lane.</param>
    /// <param name="maxMessages">At most this many.</param>
    /// <param name="wait">
    /// Whether a transport that can wait for messages (Service Bus) may wait a while on an empty lane. The pump passes
    /// <see langword="false"/> for every lane but a subscriber's last, so that an empty higher lane answers at once.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<IReadOnlyList<ITransportDelivery>> ReceiveAsync(MessageLane lane, int maxMessages, bool wait, CancellationToken cancellationToken);

    /// <summary>Takes messages the platform gave up on (its poison or dead-letter queue), for the janitor to keep as failed.</summary>
    Task<IReadOnlyList<ITransportDelivery>> ReceiveDeadLettersAsync(MessageLane lane, int maxMessages, CancellationToken cancellationToken);
}

/// <summary>A received message, locked until it is completed, abandoned or the lock runs out.</summary>
public interface ITransportDelivery
{
    /// <summary>
    /// The message. One that cannot be read has the type <see cref="MessageEnvelope.UnreadableType"/> and its raw bytes
    /// as the body, so that it ends among the failed messages rather than looping.
    /// </summary>
    MessageEnvelope Envelope { get; }

    /// <summary>How many times the platform has handed out this copy, this time included.</summary>
    int DeliveryCount { get; }

    /// <summary>How often <see cref="ExtendAsync"/> must be called while the message is handled.</summary>
    TimeSpan RenewEvery { get; }

    /// <summary>Removes the message: it was handled, retried as a copy, or kept as failed.</summary>
    Task CompleteAsync(CancellationToken cancellationToken);

    /// <summary>Releases the message at once, for another delivery.</summary>
    Task AbandonAsync(CancellationToken cancellationToken);

    /// <summary>Keeps the message locked while a long handler runs.</summary>
    Task ExtendAsync(CancellationToken cancellationToken);
}

/// <summary>What a transport can carry.</summary>
/// <param name="MaxBodyBytes">The largest payload carried inline; a larger one goes to the <see cref="IMessageStore"/>.</param>
/// <param name="MaxDelay">The longest a message can be sent dormant.</param>
/// <param name="Base64Body">Whether a compressed body grows by a third on the wire (it travels in JSON).</param>
public sealed record TransportLimits(int MaxBodyBytes, TimeSpan MaxDelay, bool Base64Body);

/// <summary>A message on its way, and where it goes.</summary>
/// <param name="Envelope">The message.</param>
/// <param name="Lanes">The lanes it goes to: every matching subscriber's, or one.</param>
/// <param name="IsDirect">
/// Whether it goes only to <paramref name="Lanes"/>' single lane rather than through routing: a retry, a replay, or a
/// scheduled message sent on. A transport that routes itself (Service Bus) must then address that subscriber alone.
/// </param>
public sealed record OutgoingMessage(MessageEnvelope Envelope, IReadOnlyList<MessageLane> Lanes, bool IsDirect);
