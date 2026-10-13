using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// Cleans up after messaging: removes expired payloads, deduplication ids, cancel markers and failed messages past the
/// retention period, and moves what the platform gave up on (its poison and dead-letter queues) into the failed
/// messages. Only one instance runs at a time.
/// </summary>
/// <remarks>
/// Run it hourly: in Azure Functions from a timer trigger, elsewhere with
/// <see cref="ServiceCollectionExtensions.AddSpineMessagingJanitor"/>.
/// </remarks>
public sealed class MessagingJanitor
{
    private const int Batch = 32;

    private readonly MessagingRuntime _runtime;
    private readonly MessageDispatcher _dispatcher;

    internal MessagingJanitor(MessagingRuntime runtime, MessageDispatcher dispatcher)
    {
        _runtime = runtime;
        _dispatcher = dispatcher;
    }

    /// <summary>Runs one cleanup.</summary>
    /// <returns>What it did; <see cref="JanitorReport.Ran"/> is <see langword="false"/> when another instance was running.</returns>
    public async Task<JanitorReport> RunAsync(CancellationToken cancellationToken = default)
    {
        await _runtime.EnsureReadyAsync(cancellationToken);

        await using var held = await _runtime.Store.TryLockAsync("janitor", cancellationToken);
        if (held is null)
        {
            _runtime.Logger.LogDebug("The messaging janitor is running elsewhere; skipped.");
            return new JanitorReport(false, 0, 0);
        }

        var purged = await _runtime.Store.PurgeAsync(_runtime.Time.GetUtcNow(), _runtime.Settings.Retention, cancellationToken);

        var rescued = 0;
        foreach (var subscriber in _runtime.Topology.Subscribers)
        foreach (var lane in subscriber.Lanes)
        {
            while (await _runtime.Transport.ReceiveDeadLettersAsync(lane, Batch, cancellationToken) is { Count: > 0 } batch)
            {
                foreach (var delivery in batch)
                {
                    await _dispatcher.FailAsync(subscriber, delivery.Envelope,
                        $"The platform gave up on the message after {delivery.DeliveryCount} deliveries (its poison or dead-letter queue): the handler crashed, hung or ran past its lock.",
                        null, cancellationToken);
                    await delivery.CompleteAsync(cancellationToken);
                    rescued++;
                }
            }
        }

        _runtime.Logger.LogInformation("Messaging janitor removed {Purged} expired items and kept {Rescued} dead-lettered messages as failed.", purged, rescued);
        return new JanitorReport(true, purged, rescued);
    }
}

/// <summary>What one janitor run did.</summary>
/// <param name="Ran">Whether it ran; <see langword="false"/> when another instance held the lock.</param>
/// <param name="Purged">Expired items removed from the store.</param>
/// <param name="Rescued">Messages moved from the platform's poison or dead-letter queues into the failed messages.</param>
public sealed record JanitorReport(bool Ran, int Purged, int Rescued);

internal sealed class JanitorService(MessagingJanitor janitor, MessagingRuntime runtime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A minute in, so a deploy's start is not slowed by a cleanup.
        var wait = TimeSpan.FromMinutes(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(wait, runtime.Time, stoppingToken);
                await janitor.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                runtime.Logger.LogError(exception, "The messaging janitor failed; it runs again in {Interval}.", runtime.Settings.JanitorInterval);
            }

            wait = runtime.Settings.JanitorInterval;
        }
    }
}
