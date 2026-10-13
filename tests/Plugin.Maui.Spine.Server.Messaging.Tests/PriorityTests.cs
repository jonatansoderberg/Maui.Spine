using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class PriorityTests
{
    [Fact]
    public async Task A_prioritised_subscriber_has_a_lane_per_priority()
    {
        await using var bus = new TestBus(m =>
        {
            m.Message<Order>();
            m.Message<Mail>();
            m.Subscriber("billing", s => s.Handles<Order>().Prioritized());
            m.Subscriber("mailer", s => s.Handles<Mail>());
        });

        Assert.Equal(["billing-high", "billing", "billing-low"], bus.Topology.GetSubscriber("billing").Lanes.Select(l => l.Name));
        Assert.Equal(["mailer"], bus.Topology.GetSubscriber("mailer").Lanes.Select(l => l.Name));

        await bus.Publisher.PublishAsync(new Order("low"), new PublishOptions { Priority = MessagePriority.Low });
        await bus.Publisher.PublishAsync(new Order("high"), new PublishOptions { Priority = MessagePriority.High });
        await bus.Publisher.PublishAsync(new Mail("x"), new PublishOptions { Priority = MessagePriority.High });

        Assert.Single(bus.Transport.Peek("billing-low"));
        Assert.Single(bus.Transport.Peek("billing-high"));
        Assert.Empty(bus.Transport.Peek("billing"));
        Assert.Single(bus.Transport.Peek("mailer"));
    }

    [Fact]
    public async Task The_pump_empties_high_before_normal_and_normal_before_low()
    {
        await using var bus = new TestBus(m =>
        {
            m.Message<Order>();
            m.Subscriber("billing", s => s.Handles<Order>().Prioritized().MaxConcurrency(1));
        }, fakeTime: false);

        foreach (var (id, priority) in new[] { ("l1", MessagePriority.Low), ("n1", MessagePriority.Normal), ("h1", MessagePriority.High), ("l2", MessagePriority.Low), ("h2", MessagePriority.High) })
            await bus.Publisher.PublishAsync(new Order(id), new PublishOptions { Priority = priority });

        await RunPumpUntilAsync(bus, () => bus.Journal.Handled.Count == 5);

        Assert.Equal(["h1", "h2", "n1", "l1", "l2"], bus.Journal.Handled.Select(h => ((Order)h.Message).Id));
    }

    internal static async Task RunPumpUntilAsync(TestBus bus, Func<bool> done, string subscriber = "billing")
    {
        IHostedService pump = new MessagePump(subscriber, bus.Services.GetRequiredService<MessagingRuntime>(), bus.Dispatcher);

        await pump.StartAsync(default);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await pump.StopAsync(default);
    }
}
