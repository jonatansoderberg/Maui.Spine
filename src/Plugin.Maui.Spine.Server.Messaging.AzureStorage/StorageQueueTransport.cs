using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.AzureStorage;

/// <summary>
/// Azure Storage Queues as the transport: one queue per lane, routed on the publishing side. Simple and cheap; messages
/// are dormant for at most 7 days at a time (longer schedules are sent on), and carry about 46 KB inline.
/// </summary>
public sealed class StorageQueueTransport : IMessageTransport
{
    private static readonly TimeSpan MaxVisibility = TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1);

    private readonly QueueServiceClient _service;
    private readonly AzureStorageMessagingOptions _options;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, QueueClient> _queues = new(StringComparer.Ordinal);

    /// <summary>Creates the transport on a queue service client.</summary>
    public StorageQueueTransport(QueueServiceClient service, AzureStorageMessagingOptions options, TimeProvider? time = null)
    {
        _service = service;
        _options = options;
        _time = time ?? TimeProvider.System;

        // 64 KB per message; Base64 leaves three quarters of it, and the envelope's own fields need the rest.
        var maxBody = (options.MessageEncoding == QueueMessageEncoding.Base64 ? 48 * 1024 : 64 * 1024) - 2 * 1024;
        Limits = new TransportLimits(maxBody, MaxVisibility, Base64Body: true);
    }

    /// <inheritdoc />
    public TransportLimits Limits { get; }

    /// <inheritdoc />
    public async Task ProvisionAsync(MessageTopology topology, CancellationToken cancellationToken)
    {
        foreach (var lane in topology.Lanes)
            StorageQueueNames.Validate(StorageQueueNames.For(_options.QueuePrefix, lane), lane);

        await Parallel.ForEachAsync(topology.Lanes, cancellationToken, async (lane, token) =>
            await Queue(StorageQueueNames.For(_options.QueuePrefix, lane)).CreateIfNotExistsAsync(cancellationToken: token));
    }

    /// <inheritdoc />
    public Task SendAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var sends = messages.SelectMany(m => m.Lanes.Select(lane => (m.Envelope, Lane: lane))).ToList();
        if (sends.Count == 1)
            return SendOneAsync(sends[0].Envelope, sends[0].Lane, now, cancellationToken);

        return Parallel.ForEachAsync(sends, new ParallelOptions { MaxDegreeOfParallelism = _options.SendParallelism, CancellationToken = cancellationToken },
            async (send, token) => await SendOneAsync(send.Envelope, send.Lane, now, token));
    }

    private async Task SendOneAsync(MessageEnvelope envelope, MessageLane lane, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var visibility = envelope.DeliverAt is { } due && due > now ? Min(due - now, MaxVisibility) : TimeSpan.Zero;

        // The queue drops the message when its time to live runs out, which must outlast the visibility delay.
        var timeToLive = envelope.ExpiresAt is { } expires ? Max(expires - now, visibility + TimeSpan.FromMinutes(1)) : TimeSpan.FromSeconds(-1);

        await Queue(StorageQueueNames.For(_options.QueuePrefix, lane))
            .SendMessageAsync(StorageQueueWire.Encode(envelope), visibility, timeToLive, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ITransportDelivery>> ReceiveAsync(MessageLane lane, int maxMessages, bool wait, CancellationToken cancellationToken) =>
        ReceiveFromAsync(StorageQueueNames.For(_options.QueuePrefix, lane), maxMessages, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ITransportDelivery>> ReceiveDeadLettersAsync(MessageLane lane, int maxMessages, CancellationToken cancellationToken) =>
        ReceiveFromAsync(StorageQueueNames.PoisonFor(StorageQueueNames.For(_options.QueuePrefix, lane)), maxMessages, cancellationToken);

    private async Task<IReadOnlyList<ITransportDelivery>> ReceiveFromAsync(string queueName, int maxMessages, CancellationToken cancellationToken)
    {
        var queue = Queue(queueName);
        QueueMessage[] messages;
        try
        {
            messages = (await queue.ReceiveMessagesAsync(Math.Clamp(maxMessages, 1, 32), _options.VisibilityTimeout, cancellationToken)).Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // A poison queue exists only once Azure Functions has poisoned a message.
            return [];
        }

        return [.. messages.Select(message => new StorageQueueDelivery(queue, message, _options.VisibilityTimeout))];
    }

    private QueueClient Queue(string name) => _queues.GetOrAdd(name, _service.GetQueueClient);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

internal sealed class StorageQueueDelivery(QueueClient queue, QueueMessage message, TimeSpan visibility) : ITransportDelivery
{
    private string _popReceipt = message.PopReceipt;

    public MessageEnvelope Envelope { get; } = StorageQueueWire.Decode(message.Body, message.MessageId);
    public int DeliveryCount => (int)message.DequeueCount;
    public TimeSpan RenewEvery => visibility / 2;

    public Task CompleteAsync(CancellationToken cancellationToken) => queue.DeleteMessageAsync(message.MessageId, _popReceipt, cancellationToken);

    public Task AbandonAsync(CancellationToken cancellationToken) => queue.UpdateMessageAsync(message.MessageId, _popReceipt, visibilityTimeout: TimeSpan.Zero, cancellationToken: cancellationToken);

    public async Task ExtendAsync(CancellationToken cancellationToken)
    {
        var receipt = await queue.UpdateMessageAsync(message.MessageId, _popReceipt, visibilityTimeout: visibility, cancellationToken: cancellationToken);
        _popReceipt = receipt.Value.PopReceipt;
    }
}
