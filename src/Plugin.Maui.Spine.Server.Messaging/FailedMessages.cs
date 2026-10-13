using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// The messages that ran out of attempts, were unrecoverable, or that the platform gave up on, for every transport in
/// one place. They are kept for the retention period.
/// </summary>
public interface IFailedMessages
{
    /// <summary>The failed messages of one subscriber, or of all.</summary>
    IAsyncEnumerable<FailedMessage> ListAsync(string? subscriber = null, CancellationToken cancellationToken = default);

    /// <summary>Sends a failed message to its subscriber again, from attempt 1, and removes it from the failed messages.</summary>
    Task ReplayAsync(FailedMessage message, CancellationToken cancellationToken = default);

    /// <summary>Removes a failed message.</summary>
    Task DeleteAsync(FailedMessage message, CancellationToken cancellationToken = default);
}

/// <summary>A message kept as failed.</summary>
/// <param name="Subscriber">The subscriber that failed it.</param>
/// <param name="Envelope">The message, with its payload inline.</param>
/// <param name="FailedAt">When it was kept.</param>
/// <param name="Error">Why.</param>
/// <param name="ExceptionType">The exception's type, when one was thrown.</param>
public sealed record FailedMessage(string Subscriber, MessageEnvelope Envelope, DateTimeOffset FailedAt, string Error, string? ExceptionType)
{
    /// <summary>The message's id.</summary>
    public string MessageId => Envelope.Id;

    /// <summary>The message type's name.</summary>
    public string Type => Envelope.Type;

    /// <summary>The attempts it had.</summary>
    public int Attempts => Envelope.Attempt;
}

internal sealed class FailedMessages(MessagingRuntime runtime, EnvelopeCodec codec) : IFailedMessages
{
    public async IAsyncEnumerable<FailedMessage> ListAsync(string? subscriber = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await runtime.EnsureReadyAsync(cancellationToken);
        await foreach (var message in runtime.Store.ListFailedAsync(subscriber, cancellationToken))
            yield return message;
    }

    public async Task ReplayAsync(FailedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await runtime.EnsureReadyAsync(cancellationToken);

        var target = runtime.Topology.GetSubscriber(message.Subscriber);
        var now = runtime.Time.GetUtcNow();
        var envelope = await codec.FitAsync(message.Envelope with
        {
            Attempt = 1,
            DeliverAt = null,
            ExpiresAt = now + runtime.Settings.Retention,
            Recipients = 1,
        }, cancellationToken);

        await runtime.Transport.SendAsync([new OutgoingMessage(envelope, [target.LaneFor(envelope.Priority)], IsDirect: true)], cancellationToken);
        await runtime.Store.DeleteFailedAsync(message, cancellationToken);
        runtime.Logger.LogInformation("Failed message {MessageId} ({Type}) replayed to {Subscriber}.", message.MessageId, message.Type, target.Name);
    }

    public async Task DeleteAsync(FailedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await runtime.EnsureReadyAsync(cancellationToken);
        await runtime.Store.DeleteFailedAsync(message, cancellationToken);
    }
}
