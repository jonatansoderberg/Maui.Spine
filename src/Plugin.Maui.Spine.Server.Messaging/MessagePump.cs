using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// Handles one subscriber's messages in the host's own process: receives from its lanes, highest priority first, runs up
/// to <see cref="Subscriber.MaxConcurrency"/> handlers at a time, keeps their locks alive and completes them.
/// </summary>
internal sealed class MessagePump(string subscriberName, MessagingRuntime runtime, MessageDispatcher dispatcher) : BackgroundService
{
    private static readonly TimeSpan FirstIdleWait = TimeSpan.FromMilliseconds(50);
    private const int MaxBatch = 32;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = runtime.Topology.GetSubscriber(subscriberName);
        await WaitUntilReadyAsync(stoppingToken);

        // Receives again once at least half the handlers are free, so a busy pump fetches in batches rather than one
        // message per finished handler.
        var capacity = subscriber.MaxConcurrency;
        var threshold = Math.Max(1, capacity / 2);
        var inFlight = 0;
        using var finished = new SemaphoreSlim(0);
        var idle = FirstIdleWait;
        runtime.Logger.LogInformation("Message pump for {Subscriber} started: {Lanes}, {Concurrency} at a time.",
            subscriber.Name, string.Join(", ", subscriber.Lanes.Select(l => l.Name)), capacity);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (capacity - Volatile.Read(ref inFlight) < threshold)
                    await finished.WaitAsync(stoppingToken);

                IReadOnlyList<ITransportDelivery> batch = [];
                try
                {
                    // Strict priority: a lower lane is read only when every higher one is empty.
                    var free = Math.Min(MaxBatch, capacity - Volatile.Read(ref inFlight));
                    foreach (var lane in subscriber.Lanes)
                    {
                        batch = await runtime.Transport.ReceiveAsync(lane, free, stoppingToken);
                        if (batch.Count > 0)
                            break;
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    runtime.Logger.LogError(exception, "Message pump for {Subscriber} could not receive; trying again in {Wait}.", subscriber.Name, runtime.Settings.MaxPollInterval);
                    await Task.Delay(runtime.Settings.MaxPollInterval, runtime.Time, stoppingToken);
                    continue;
                }

                if (batch.Count == 0)
                {
                    await Task.Delay(idle, runtime.Time, stoppingToken);
                    idle = TimeSpan.FromTicks(Math.Min(idle.Ticks * 2, runtime.Settings.MaxPollInterval.Ticks));
                    continue;
                }

                idle = FirstIdleWait;
                foreach (var delivery in batch)
                {
                    Interlocked.Increment(ref inFlight);
                    _ = HandleAsync(subscriber, delivery, stoppingToken).ContinueWith(_ =>
                    {
                        Interlocked.Decrement(ref inFlight);
                        finished.Release();
                    }, TaskScheduler.Default);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        // Lets the handlers in flight finish or give their messages back.
        var deadline = runtime.Time.GetUtcNow() + TimeSpan.FromSeconds(30);
        while (Volatile.Read(ref inFlight) > 0 && runtime.Time.GetUtcNow() < deadline)
            await finished.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
    }

    private async Task WaitUntilReadyAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                await runtime.EnsureReadyAsync(stoppingToken);
                return;
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(runtime.Settings.MaxPollInterval, runtime.Time, stoppingToken);
            }
        }
    }

    private async Task HandleAsync(Subscriber subscriber, ITransportDelivery delivery, CancellationToken stoppingToken)
    {
        using var renewing = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewal = RenewAsync(delivery, renewing.Token);

        try
        {
            await dispatcher.DispatchAsync(subscriber.Name, delivery.Envelope, delivery.DeliveryCount, stoppingToken);
            await renewing.CancelAsync();
            await renewal;
            await delivery.CompleteAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            await renewing.CancelAsync();
            await renewal;
            if (!stoppingToken.IsCancellationRequested)
                runtime.Logger.LogError(exception, "Message {MessageId} for {Subscriber} could not be dispatched; it goes back to the queue.", delivery.Envelope.Id, subscriber.Name);
            await GiveBackAsync(delivery);
        }
    }

    private async Task RenewAsync(ITransportDelivery delivery, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(delivery.RenewEvery, runtime.Time, cancellationToken);
                await delivery.ExtendAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            runtime.Logger.LogWarning(exception, "The lock on message {MessageId} could not be extended; it may be handed out again.", delivery.Envelope.Id);
        }
    }

    private async Task GiveBackAsync(ITransportDelivery delivery)
    {
        try
        {
            await delivery.AbandonAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            runtime.Logger.LogWarning(exception, "Message {MessageId} could not be given back; it returns when its lock runs out.", delivery.Envelope.Id);
        }
    }
}
