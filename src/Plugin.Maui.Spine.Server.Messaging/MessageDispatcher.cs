using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// Hands a received message to its handler, for the pump and for Azure Functions alike: reads an offloaded payload back,
/// drops cancelled and expired messages, sends on a scheduled message that surfaced early, and on failure sends a retry
/// or keeps the message as failed.
/// </summary>
/// <remarks>
/// When <see cref="DispatchAsync"/> returns, the delivery is done with and can be completed, whatever the handler did.
/// It throws only when messaging itself could not do its part (the transport or the store failed), and the delivery
/// should then go back to the queue. In Azure Functions, call the transport package's <c>DispatchAsync</c> overload for
/// the trigger's message type.
/// </remarks>
public sealed class MessageDispatcher
{
    private static readonly TimeSpan EarlyTolerance = TimeSpan.FromSeconds(1);

    private readonly MessagingRuntime _runtime;
    private readonly EnvelopeCodec _codec;
    private readonly IServiceScopeFactory _scopes;

    internal MessageDispatcher(MessagingRuntime runtime, EnvelopeCodec codec, IServiceScopeFactory scopes)
    {
        _runtime = runtime;
        _codec = codec;
        _scopes = scopes;
    }

    /// <summary>Handles one delivery of <paramref name="envelope"/> for <paramref name="subscriber"/>.</summary>
    /// <param name="subscriber">The subscriber whose lane it came from.</param>
    /// <param name="envelope">The message.</param>
    /// <param name="deliveryCount">How many times the platform has handed this copy out, this time included.</param>
    /// <param name="cancellationToken">The host stopping. The message then goes back to the queue.</param>
    public async Task DispatchAsync(string subscriber, MessageEnvelope envelope, int deliveryCount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var target = _runtime.Topology.GetSubscriber(subscriber);
        await _runtime.EnsureReadyAsync(cancellationToken);

        using var activity = MessagingTelemetry.Source.StartActivity($"process {envelope.Type}", ActivityKind.Consumer, envelope.TraceParent);
        activity?.SetTag("messaging.message.id", envelope.Id);
        activity?.SetTag("messaging.subscriber", target.Name);
        activity?.SetTag("messaging.attempt", envelope.Attempt);

        var now = _runtime.Time.GetUtcNow();
        var lane = target.LaneFor(envelope.Priority);

        if (envelope.ExpiresAt is { } expiresAt && now > expiresAt)
        {
            _runtime.Logger.LogWarning("Message {MessageId} ({Type}) for {Subscriber} expired at {ExpiresAt:O} unhandled and was dropped.",
                envelope.Id, envelope.Type, target.Name, expiresAt);
            await ReleasePayloadAsync(envelope);
            return;
        }

        if (deliveryCount > _runtime.Settings.MaxDeliveries)
        {
            await FailAsync(target, envelope,
                $"Delivered {deliveryCount} times without completing: the handler crashed, hung or ran past its lock.", null, cancellationToken);
            return;
        }

        if (envelope.DeliverAt is { } due && due > now + EarlyTolerance)
        {
            // A transport that cannot hold a message as long as asked (Storage Queues: 7 days) surfaces it early.
            await _runtime.Transport.SendAsync([new OutgoingMessage(envelope, [lane], IsDirect: true)], cancellationToken);
            return;
        }

        if (envelope.DeliverAt is { } scheduled && await _runtime.Store.IsCancelledAsync(envelope.Id, scheduled, cancellationToken))
        {
            _runtime.Logger.LogInformation("Message {MessageId} ({Type}) for {Subscriber} was cancelled and was dropped.", envelope.Id, envelope.Type, target.Name);
            await ReleasePayloadAsync(envelope);
            return;
        }

        var type = _runtime.Topology.FindType(envelope.Type);
        if (type is null || !target.Types.Contains(type))
        {
            var reason = envelope.Type == MessageEnvelope.UnreadableType
                ? "The transport could not read the message."
                : type is null
                    ? $"'{envelope.Type}' is not a registered message type in this host."
                    : $"Subscriber '{target.Name}' does not handle '{envelope.Type}'.";
            await FailAsync(target, envelope, reason, null, cancellationToken);
            return;
        }

        object message;
        try
        {
            if (await _codec.UnpackAsync(envelope, cancellationToken) is not { } json)
            {
                await FailAsync(target, envelope, $"The offloaded payload {envelope.Payload?.Location} is gone.", null, cancellationToken);
                return;
            }
            message = type.Deserialize(json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or UnrecoverableMessageException or InvalidDataException)
        {
            await FailAsync(target, envelope, $"The payload could not be read as {type.ClrType.Name}: {exception.Message}", exception, cancellationToken);
            return;
        }

        var context = new MessageContext
        {
            MessageId = envelope.Id,
            Type = envelope.Type,
            Subscriber = target.Name,
            Metadata = envelope.Metadata,
            Priority = envelope.Priority,
            Attempt = envelope.Attempt,
            EnqueuedAt = envelope.EnqueuedAt,
            DeliverAt = envelope.DeliverAt,
            CorrelationId = envelope.CorrelationId,
        };

        var tags = MessagingTelemetry.Tags(envelope, target.Name);
        MessagingTelemetry.Latency.Record((now - (envelope.DeliverAt ?? envelope.EnqueuedAt)).TotalMilliseconds, tags);
        var started = _runtime.Time.GetTimestamp();

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await type.HandleAsync(scope.ServiceProvider, message, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            await RetryOrFailAsync(target, lane, envelope, exception, cancellationToken);
            return;
        }
        finally
        {
            MessagingTelemetry.HandleDuration.Record(_runtime.Time.GetElapsedTime(started).TotalMilliseconds, tags);
        }

        MessagingTelemetry.Handled.Add(1, tags);
        await ReleasePayloadAsync(envelope);
    }

    private async Task RetryOrFailAsync(Subscriber target, MessageLane lane, MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is UnrecoverableMessageException)
        {
            await FailAsync(target, envelope, exception.Message, exception, cancellationToken);
            return;
        }

        if (envelope.Attempt >= target.Retry.MaxAttempts)
        {
            await FailAsync(target, envelope, $"Attempt {envelope.Attempt} of {target.Retry.MaxAttempts} failed: {exception.Message}", exception, cancellationToken);
            return;
        }

        var retryAt = _runtime.Time.GetUtcNow() + target.Retry.DelayAfter(envelope.Attempt);
        if (envelope.ExpiresAt is { } expiresAt && retryAt >= expiresAt)
        {
            await FailAsync(target, envelope, $"Attempt {envelope.Attempt} failed and the message expires before the next: {exception.Message}", exception, cancellationToken);
            return;
        }

        _runtime.Logger.LogWarning(exception, "Message {MessageId} ({Type}) for {Subscriber} failed attempt {Attempt} of {MaxAttempts}; trying again at {RetryAt:O}.",
            envelope.Id, envelope.Type, target.Name, envelope.Attempt, target.Retry.MaxAttempts, retryAt);
        MessagingTelemetry.Retried.Add(1, MessagingTelemetry.Tags(envelope, target.Name));

        var retry = envelope with { Attempt = envelope.Attempt + 1, DeliverAt = retryAt };
        await _runtime.Transport.SendAsync([new OutgoingMessage(retry, [lane], IsDirect: true)], cancellationToken);
    }

    internal async Task FailAsync(Subscriber target, MessageEnvelope envelope, string error, Exception? exception, CancellationToken cancellationToken)
    {
        var inline = await _codec.InlineAsync(envelope, cancellationToken);
        var failed = new FailedMessage(target.Name, inline, _runtime.Time.GetUtcNow(), error, exception?.GetType().FullName);
        await _runtime.Store.SaveFailedAsync(failed, cancellationToken);

        _runtime.Logger.LogError(exception, "Message {MessageId} ({Type}) for {Subscriber} failed after {Attempts} attempts and was kept as failed: {Error}",
            envelope.Id, envelope.Type, target.Name, envelope.Attempt, error);
        MessagingTelemetry.Failed.Add(1, MessagingTelemetry.Tags(envelope, target.Name));

        if (inline.Payload is null)
            await ReleasePayloadAsync(envelope);
    }

    private async Task ReleasePayloadAsync(MessageEnvelope envelope)
    {
        if (envelope.Payload is not { } payload || envelope.Recipients != 1)
            return;

        try
        {
            await _runtime.Store.DeletePayloadAsync(payload, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _runtime.Logger.LogWarning(exception, "The payload {Location} of message {MessageId} could not be deleted; the janitor removes it when it expires.",
                payload.Location, envelope.Id);
        }
    }
}
