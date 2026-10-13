using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging;

internal sealed class MessagePublisher(MessagingRuntime runtime, EnvelopeCodec codec) : IMessagePublisher
{
    public async Task<PublishResult> PublishAsync<T>(T message, PublishOptions? options = null, CancellationToken cancellationToken = default) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        options ??= PublishOptions.Default;

        var (result, outgoing) = await PrepareAsync(message, options, cancellationToken);
        if (outgoing is null)
            return result;

        try
        {
            await runtime.Transport.SendAsync([outgoing], cancellationToken);
        }
        catch when (options.DeduplicationId is { } deduplicationId)
        {
            // Not sent, so not a duplicate when the caller tries again.
            await ReleaseAsync(deduplicationId);
            throw;
        }
        return result;
    }

    public async Task<IReadOnlyList<PublishResult>> PublishBatchAsync<T>(IEnumerable<T> messages, PublishOptions? options = null, CancellationToken cancellationToken = default) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(messages);
        options ??= PublishOptions.Default;
        if (options.MessageId is not null || options.DeduplicationId is not null)
            throw new ArgumentException("A batch cannot set MessageId or DeduplicationId: every message needs its own.", nameof(options));

        var results = new List<PublishResult>();
        var outgoing = new List<OutgoingMessage>();
        foreach (var message in messages)
        {
            var (result, prepared) = await PrepareAsync(message, options, cancellationToken);
            results.Add(result);
            if (prepared is not null)
                outgoing.Add(prepared);
        }

        if (outgoing.Count > 0)
            await runtime.Transport.SendAsync(outgoing, cancellationToken);
        return results;
    }

    public async Task CancelAsync(string messageId, DateTimeOffset deliverAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);
        await runtime.EnsureReadyAsync(cancellationToken);
        await runtime.Store.CancelAsync(messageId, deliverAt, cancellationToken);
        runtime.Logger.LogInformation("Scheduled message {MessageId} due {DeliverAt:O} cancelled.", messageId, deliverAt);
    }

    private async Task ReleaseAsync(string deduplicationId)
    {
        try
        {
            await runtime.Store.ReleaseReservationAsync(deduplicationId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            runtime.Logger.LogWarning(exception, "Deduplication id {DeduplicationId} of a failed publish could not be released; a retry is dropped as a duplicate until its window ends.", deduplicationId);
        }
    }

    private async Task<(PublishResult, OutgoingMessage?)> PrepareAsync<T>(T message, PublishOptions options, CancellationToken cancellationToken) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var key in options.Metadata.Keys)
            MetadataRules.ValidateKey(key);
        MetadataRules.ValidateSize(options.Metadata);

        await runtime.EnsureReadyAsync(cancellationToken);

        var type = runtime.Topology.TypeOf(message.GetType(), typeof(T));
        var now = runtime.Time.GetUtcNow();
        var id = options.MessageId ?? Guid.CreateVersion7(now).ToString("N");
        DateTimeOffset? due = options.DeliverAt ?? (options.Delay is { } delay ? now + delay : null);
        if (due <= now)
            due = null;

        using var activity = MessagingTelemetry.Source.StartActivity($"publish {type.Name}", ActivityKind.Producer);
        activity?.SetTag("messaging.message.id", id);
        activity?.SetTag("messaging.message.type", type.Name);

        var metadata = new Dictionary<string, string>(options.Metadata, StringComparer.Ordinal);
        var lanes = runtime.Topology.Route(type.Name, metadata, options.Priority);
        if (lanes.Count == 0)
        {
            runtime.Logger.LogWarning("Message {MessageId} of type {Type} matched no subscriber and was not sent.", id, type.Name);
            return (new PublishResult(id, due, [], false), null);
        }

        if (options.DeduplicationId is { } deduplicationId
            && !await runtime.Store.TryReserveAsync(deduplicationId, now + options.DeduplicationWindow, cancellationToken))
        {
            runtime.Logger.LogInformation("Message of type {Type} with deduplication id {DeduplicationId} was published before and was dropped.", type.Name, deduplicationId);
            return (new PublishResult(id, due, [], true), null);
        }

        MessageEnvelope envelope;
        try
        {
            envelope = await codec.PackAsync(new MessageEnvelope
            {
                Id = id,
                Type = type.Name,
                Metadata = metadata,
                Priority = options.Priority,
                EnqueuedAt = now,
                DeliverAt = due,
                ExpiresAt = (due ?? now) + (options.TimeToLive ?? runtime.Settings.Retention),
                CorrelationId = options.CorrelationId,
                TraceParent = Activity.Current?.Id,
                Recipients = lanes.Count,
            }, type.Serialize(message), cancellationToken);
        }
        catch when (options.DeduplicationId is { } reserved)
        {
            await ReleaseAsync(reserved);
            throw;
        }

        MessagingTelemetry.Published.Add(lanes.Count, MessagingTelemetry.Tags(envelope));
        return (new PublishResult(id, due, [.. lanes.Select(l => l.Subscriber)], false), new OutgoingMessage(envelope, lanes, IsDirect: false));
    }
}
