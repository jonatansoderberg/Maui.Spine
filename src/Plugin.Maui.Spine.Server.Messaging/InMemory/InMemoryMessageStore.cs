using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.InMemory;

/// <summary>Payloads, failed messages and markers in the process, for tests and for running without storage.</summary>
public sealed class InMemoryMessageStore(TimeProvider? time = null) : IMessageStore
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (ReadOnlyMemory<byte> Body, DateTimeOffset ExpiresAt)> _payloads = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _reservations = new();
    private readonly ConcurrentDictionary<(string Id, DateTimeOffset Due), byte> _cancelled = new();
    private readonly ConcurrentDictionary<(string Subscriber, string Id), FailedMessage> _failed = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>How many payloads it holds.</summary>
    public int PayloadCount => _payloads.Count;

    /// <inheritdoc />
    public Task ProvisionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<PayloadReference> PutPayloadAsync(string messageId, ReadOnlyMemory<byte> body, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var location = $"payloads/{messageId}";
        _payloads[location] = (body.ToArray(), expiresAt);
        return Task.FromResult(new PayloadReference(location, body.Length));
    }

    /// <inheritdoc />
    public Task<ReadOnlyMemory<byte>?> GetPayloadAsync(PayloadReference payload, CancellationToken cancellationToken) =>
        Task.FromResult<ReadOnlyMemory<byte>?>(_payloads.TryGetValue(payload.Location, out var stored) ? stored.Body : null);

    /// <inheritdoc />
    public Task DeletePayloadAsync(PayloadReference payload, CancellationToken cancellationToken)
    {
        _payloads.TryRemove(payload.Location, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryReserveAsync(string deduplicationId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        while (true)
        {
            if (_reservations.TryAdd(deduplicationId, expiresAt))
                return Task.FromResult(true);
            if (_reservations.TryGetValue(deduplicationId, out var held) && held > now)
                return Task.FromResult(false);
            if (_reservations.TryUpdate(deduplicationId, expiresAt, held))
                return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task ReleaseReservationAsync(string deduplicationId, CancellationToken cancellationToken)
    {
        _reservations.TryRemove(deduplicationId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CancelAsync(string messageId, DateTimeOffset deliverAt, CancellationToken cancellationToken)
    {
        _cancelled[(messageId, deliverAt)] = 0;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsCancelledAsync(string messageId, DateTimeOffset deliverAt, CancellationToken cancellationToken) =>
        Task.FromResult(_cancelled.ContainsKey((messageId, deliverAt)));

    /// <inheritdoc />
    public Task SaveFailedAsync(FailedMessage message, CancellationToken cancellationToken)
    {
        _failed[(message.Subscriber, message.MessageId)] = message;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FailedMessage> ListFailedAsync(string? subscriber, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var message in _failed.Values.Where(m => subscriber is null || m.Subscriber == subscriber).OrderBy(m => m.FailedAt))
            yield return message;
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteFailedAsync(FailedMessage message, CancellationToken cancellationToken)
    {
        _failed.TryRemove((message.Subscriber, message.MessageId), out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> TryLockAsync(string name, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(name, _ => new SemaphoreSlim(1));
        return await gate.WaitAsync(0, cancellationToken) ? new Release(gate) : null;
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(DateTimeOffset now, TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoff = now - retention;
        var removed = 0;
        removed += RemoveWhere(_payloads, p => p.Value.ExpiresAt < now);
        removed += RemoveWhere(_reservations, r => r.Value < now);
        removed += RemoveWhere(_cancelled, c => c.Key.Due < cutoff);
        removed += RemoveWhere(_failed, f => f.Value.FailedAt < cutoff);
        return Task.FromResult(removed);
    }

    private static int RemoveWhere<TKey, TValue>(ConcurrentDictionary<TKey, TValue> items, Func<KeyValuePair<TKey, TValue>, bool> expired) where TKey : notnull =>
        items.Where(expired).Count(item => items.TryRemove(item.Key, out _));

    private sealed class Release(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
