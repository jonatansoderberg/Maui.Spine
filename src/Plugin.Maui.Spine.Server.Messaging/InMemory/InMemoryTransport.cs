using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.InMemory;

/// <summary>
/// Queues in the process, for tests and for running without storage. Messages are lost on restart and not shared
/// between instances. It behaves like the real transports where tests need it to: dormant messages, locks that run out,
/// and a dead-letter queue after too many deliveries.
/// </summary>
public sealed class InMemoryTransport : IMessageTransport
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time;
    private readonly int _deadLetterAfter;
    private readonly Dictionary<string, List<Entry>> _lanes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Entry>> _deadLetters = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Creates the transport.</summary>
    /// <param name="time">The clock; a fake one lets tests move scheduled messages along.</param>
    /// <param name="limits">What it carries inline. Unset, 256 KB and no limit on delays.</param>
    /// <param name="deadLetterAfter">Deliveries before a message goes to the dead-letter queue, as Service Bus's MaxDeliveryCount.</param>
    public InMemoryTransport(TimeProvider? time = null, TransportLimits? limits = null, int deadLetterAfter = 10)
    {
        _time = time ?? TimeProvider.System;
        Limits = limits ?? new TransportLimits(256 * 1024, TimeSpan.MaxValue, Base64Body: false);
        _deadLetterAfter = deadLetterAfter;
    }

    /// <inheritdoc />
    public TransportLimits Limits { get; }

    /// <summary>Every message in a lane, dormant and locked ones included, in the order they were sent.</summary>
    public IReadOnlyList<MessageEnvelope> Peek(string lane)
    {
        lock (_gate)
            return _lanes.TryGetValue(lane, out var entries) ? [.. entries.Select(e => e.Envelope)] : [];
    }

    /// <summary>Every message in a lane's dead-letter queue.</summary>
    public IReadOnlyList<MessageEnvelope> PeekDeadLetters(string lane)
    {
        lock (_gate)
            return _deadLetters.TryGetValue(lane, out var entries) ? [.. entries.Select(e => e.Envelope)] : [];
    }

    /// <inheritdoc />
    public Task ProvisionAsync(MessageTopology topology, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var lane in topology.Lanes)
            {
                _lanes.TryAdd(lane.Name, []);
                _deadLetters.TryAdd(lane.Name, []);
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            foreach (var message in messages)
            foreach (var lane in message.Lanes)
            {
                var visibleAt = message.Envelope.DeliverAt is { } due && due > now
                    ? (Limits.MaxDelay == TimeSpan.MaxValue || due - now <= Limits.MaxDelay ? due : now + Limits.MaxDelay)
                    : now;
                LaneOf(_lanes, lane.Name).Add(new Entry(message.Envelope) { VisibleAt = visibleAt });
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ITransportDelivery>> ReceiveAsync(MessageLane lane, int maxMessages, CancellationToken cancellationToken) =>
        Task.FromResult(Take(_lanes, lane.Name, maxMessages, deadLetter: true));

    /// <inheritdoc />
    public Task<IReadOnlyList<ITransportDelivery>> ReceiveDeadLettersAsync(MessageLane lane, int maxMessages, CancellationToken cancellationToken) =>
        Task.FromResult(Take(_deadLetters, lane.Name, maxMessages, deadLetter: false));

    private IReadOnlyList<ITransportDelivery> Take(Dictionary<string, List<Entry>> queues, string lane, int maxMessages, bool deadLetter)
    {
        var now = _time.GetUtcNow();
        var taken = new List<ITransportDelivery>();
        lock (_gate)
        {
            var entries = LaneOf(queues, lane);
            foreach (var entry in entries.Where(e => e.VisibleAt <= now && (e.LockedUntil is null || e.LockedUntil <= now)).OrderBy(e => e.VisibleAt).ToList())
            {
                if (taken.Count == maxMessages)
                    break;

                if (deadLetter && entry.DeliveryCount >= _deadLetterAfter)
                {
                    entries.Remove(entry);
                    LaneOf(_deadLetters, lane).Add(new Entry(entry.Envelope) { VisibleAt = now, DeliveryCount = entry.DeliveryCount });
                    continue;
                }

                entry.DeliveryCount++;
                entry.LockedUntil = now + LockDuration;
                taken.Add(new Delivery(this, entries, entry));
            }
        }
        return taken;
    }

    private static List<Entry> LaneOf(Dictionary<string, List<Entry>> queues, string lane)
    {
        if (!queues.TryGetValue(lane, out var entries))
            queues[lane] = entries = [];
        return entries;
    }

    private sealed class Entry(MessageEnvelope envelope)
    {
        public MessageEnvelope Envelope { get; } = envelope;
        public DateTimeOffset VisibleAt { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
        public int DeliveryCount { get; set; }
    }

    private sealed class Delivery(InMemoryTransport transport, List<Entry> queue, Entry entry) : ITransportDelivery
    {
        public MessageEnvelope Envelope => entry.Envelope;
        public int DeliveryCount { get; } = entry.DeliveryCount;
        public TimeSpan RenewEvery => TimeSpan.FromMinutes(2);

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            lock (transport._gate)
                queue.Remove(entry);
            return Task.CompletedTask;
        }

        public Task AbandonAsync(CancellationToken cancellationToken)
        {
            lock (transport._gate)
                entry.LockedUntil = null;
            return Task.CompletedTask;
        }

        public Task ExtendAsync(CancellationToken cancellationToken)
        {
            lock (transport._gate)
                entry.LockedUntil = transport._time.GetUtcNow() + LockDuration;
            return Task.CompletedTask;
        }
    }
}
