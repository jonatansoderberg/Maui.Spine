using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Server.Messaging.InMemory;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed record Order(string Id, int Lines = 1);
public sealed record Mail(string To, string Text = "");
public sealed record Big(string Text);

/// <summary>What the handlers saw, in order.</summary>
public sealed class Journal
{
    public ConcurrentQueue<(string Subscriber, object Message, MessageContext Context)> Handled { get; } = new();

    /// <summary>How many times a handler should throw for a message id before it succeeds; <c>-1</c> for always.</summary>
    public ConcurrentDictionary<string, int> Failures { get; } = new();

    public bool Unrecoverable { get; set; }

    public IReadOnlyList<MessageContext> Contexts => [.. Handled.Select(h => h.Context)];

    public void Record(object message, MessageContext context)
    {
        if (Failures.TryGetValue(context.MessageId, out var left) && left != 0)
        {
            Failures[context.MessageId] = left - 1;
            throw Unrecoverable ? new UnrecoverableMessageException("Cannot be fixed.") : new InvalidOperationException($"Planned failure {context.Attempt}.");
        }
        Handled.Enqueue((context.Subscriber, message, context));
    }
}

public sealed class OrderHandler(Journal journal) : IMessageHandler<Order>
{
    public Task HandleAsync(Order message, MessageContext context, CancellationToken cancellationToken)
    {
        journal.Record(message, context);
        return Task.CompletedTask;
    }
}

public sealed class MailHandler(Journal journal) : IMessageHandler<Mail>
{
    public Task HandleAsync(Mail message, MessageContext context, CancellationToken cancellationToken)
    {
        journal.Record(message, context);
        return Task.CompletedTask;
    }
}

public sealed class BigHandler(Journal journal) : IMessageHandler<Big>
{
    public Task HandleAsync(Big message, MessageContext context, CancellationToken cancellationToken)
    {
        journal.Record(message, context);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Messaging in memory on a fake clock, with a <see cref="DrainAsync"/> that does what the pump does, one message at a
/// time, so tests are deterministic.
/// </summary>
public sealed class TestBus : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;

    public TestBus(Action<MessagingBuilder> configure, TransportLimits? limits = null, bool fakeTime = true)
    {
        Time = fakeTime ? new FakeTimeProvider(Start) : null;
        var services = new ServiceCollection();
        services.AddLogging();
        if (Time is not null)
            services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton(Journal);
        services.AddSpineMessaging(m =>
        {
            m.UseInMemory(limits);
            configure(m);
        });
        services.AddMessageHandler<Order, OrderHandler>();
        services.AddMessageHandler<Mail, MailHandler>();
        services.AddMessageHandler<Big, BigHandler>();
        _services = services.BuildServiceProvider();
    }

    public FakeTimeProvider? Time { get; }
    public Journal Journal { get; } = new();
    public IServiceProvider Services => _services;
    public IMessagePublisher Publisher => _services.GetRequiredService<IMessagePublisher>();
    public MessageDispatcher Dispatcher => _services.GetRequiredService<MessageDispatcher>();
    public IFailedMessages Failed => _services.GetRequiredService<IFailedMessages>();
    public MessagingJanitor Janitor => _services.GetRequiredService<MessagingJanitor>();
    public InMemoryTransport Transport => _services.GetRequiredService<InMemoryTransport>();
    public InMemoryMessageStore Store => _services.GetRequiredService<InMemoryMessageStore>();
    public MessageTopology Topology => _services.GetRequiredService<MessageTopology>();

    /// <summary>Receives and dispatches until every lane of <paramref name="subscriber"/> is empty at the current time.</summary>
    /// <returns>How many deliveries it handled.</returns>
    public async Task<int> DrainAsync(string subscriber, bool complete = true)
    {
        var count = 0;
        var lanes = Topology.GetSubscriber(subscriber).Lanes;
        while (true)
        {
            ITransportDelivery? delivery = null;
            foreach (var lane in lanes)
            {
                if ((await Transport.ReceiveAsync(lane, 1, wait: false, default)).FirstOrDefault() is { } taken)
                {
                    delivery = taken;
                    break;
                }
            }

            if (delivery is null)
                return count;

            await Dispatcher.DispatchAsync(subscriber, delivery.Envelope, delivery.DeliveryCount, default);
            if (complete)
                await delivery.CompleteAsync(default);
            count++;
        }
    }

    public async Task<List<FailedMessage>> FailedOfAsync(string? subscriber = null)
    {
        var list = new List<FailedMessage>();
        await foreach (var message in Failed.ListAsync(subscriber))
            list.Add(message);
        return list;
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
