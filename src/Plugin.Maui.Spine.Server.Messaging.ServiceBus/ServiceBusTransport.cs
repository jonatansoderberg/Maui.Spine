using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.ServiceBus;

/// <summary>
/// Azure Service Bus as the transport: one topic, a subscription per lane, and correlation filters that route each
/// message to the subscribers that take it. Scheduling is native and has no limit.
/// </summary>
public sealed class ServiceBusTransport : IMessageTransport, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusAdministrationClient? _admin;
    private readonly ServiceBusMessagingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ServiceBusSender _sender;
    private readonly ConcurrentDictionary<(string Lane, bool DeadLetter), ServiceBusReceiver> _receivers = new();

    /// <summary>Creates the transport.</summary>
    /// <param name="client">The client for sending and receiving.</param>
    /// <param name="admin">The administration client for provisioning; <see langword="null"/> when <see cref="ServiceBusMessagingOptions.Provision"/> is off.</param>
    /// <param name="options">Topic, limits, locks.</param>
    /// <param name="logger">Where provisioning is logged.</param>
    /// <param name="time">The clock.</param>
    public ServiceBusTransport(ServiceBusClient client, ServiceBusAdministrationClient? admin, ServiceBusMessagingOptions options, ILogger<ServiceBusTransport> logger, TimeProvider? time = null)
    {
        _client = client;
        _admin = admin;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _sender = client.CreateSender(options.TopicName);
        Limits = new TransportLimits(options.MaxBodyBytes, TimeSpan.MaxValue, Base64Body: false);
    }

    /// <inheritdoc />
    public TransportLimits Limits { get; }

    /// <inheritdoc />
    public async Task ProvisionAsync(MessageTopology topology, CancellationToken cancellationToken)
    {
        if (_admin is null || !_options.Provision)
            return;

        try
        {
            await new ServiceBusProvisioner(_admin, _options, _logger).ProvisionAsync(topology, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Service Bus provisioning of topic '{_options.TopicName}' failed on {_client.FullyQualifiedNamespace}: {exception.Message} " +
                "The identity needs the Manage right; otherwise set Provision = false and create the entities with infrastructure as code. " +
                "On the emulator, set EmulatorManagementEndpoint to its management port (Aspire: the 'emulatorhealth' endpoint).",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (messages.Count == 1)
        {
            await _sender.SendMessageAsync(ServiceBusEnvelopeMapper.ToMessage(messages[0], now), cancellationToken);
            return;
        }

        var batch = await _sender.CreateMessageBatchAsync(cancellationToken);
        try
        {
            foreach (var outgoing in messages)
            {
                var message = ServiceBusEnvelopeMapper.ToMessage(outgoing, now);
                if (batch.TryAddMessage(message))
                    continue;

                if (batch.Count > 0)
                {
                    await _sender.SendMessagesAsync(batch, cancellationToken);
                    batch.Dispose();
                    batch = await _sender.CreateMessageBatchAsync(cancellationToken);
                }

                if (!batch.TryAddMessage(message))
                    await _sender.SendMessageAsync(message, cancellationToken);
            }

            if (batch.Count > 0)
                await _sender.SendMessagesAsync(batch, cancellationToken);
        }
        finally
        {
            batch.Dispose();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ITransportDelivery>> ReceiveAsync(MessageLane lane, int maxMessages, CancellationToken cancellationToken) =>
        ReceiveFromAsync(lane, deadLetter: false, maxMessages, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ITransportDelivery>> ReceiveDeadLettersAsync(MessageLane lane, int maxMessages, CancellationToken cancellationToken) =>
        ReceiveFromAsync(lane, deadLetter: true, maxMessages, cancellationToken);

    private async Task<IReadOnlyList<ITransportDelivery>> ReceiveFromAsync(MessageLane lane, bool deadLetter, int maxMessages, CancellationToken cancellationToken)
    {
        var receiver = _receivers.GetOrAdd((lane.Name, deadLetter), key => _client.CreateReceiver(_options.TopicName, key.Lane,
            new ServiceBusReceiverOptions { SubQueue = key.DeadLetter ? SubQueue.DeadLetter : SubQueue.None }));

        var messages = await receiver.ReceiveMessagesAsync(Math.Max(1, maxMessages), _options.ReceiveWait, cancellationToken);
        return [.. messages.Select(message => new ServiceBusDelivery(receiver, message, _options.LockDuration))];
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var receiver in _receivers.Values)
            await receiver.DisposeAsync();
        await _sender.DisposeAsync();
    }
}

internal sealed class ServiceBusDelivery(ServiceBusReceiver receiver, ServiceBusReceivedMessage message, TimeSpan lockDuration) : ITransportDelivery
{
    public MessageEnvelope Envelope { get; } = ServiceBusEnvelopeMapper.ToEnvelope(message);
    public int DeliveryCount => message.DeliveryCount;
    public TimeSpan RenewEvery => lockDuration / 2;

    public Task CompleteAsync(CancellationToken cancellationToken) => receiver.CompleteMessageAsync(message, cancellationToken);

    public Task AbandonAsync(CancellationToken cancellationToken) => receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken);

    public Task ExtendAsync(CancellationToken cancellationToken) => receiver.RenewMessageLockAsync(message, cancellationToken);
}
