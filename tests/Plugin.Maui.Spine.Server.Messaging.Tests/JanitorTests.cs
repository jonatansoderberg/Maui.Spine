using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class JanitorTests
{
    private static void Billing(MessagingBuilder m)
    {
        m.Retention = TimeSpan.FromDays(3);
        m.Message<Order>();
        m.Subscriber("billing", s => s.Handles<Order>().Retry(1));
    }

    [Fact]
    public async Task Failed_messages_and_markers_go_after_the_retention_period()
    {
        await using var bus = new TestBus(Billing);
        var order = await bus.Publisher.PublishAsync(new Order("o1"), new PublishOptions { DeduplicationId = "d1", DeduplicationWindow = TimeSpan.FromHours(1) });
        bus.Journal.Failures[order.MessageId] = -1;
        await bus.DrainAsync("billing");
        var scheduled = await bus.Publisher.PublishAsync(new Order("o2"), new PublishOptions { Delay = TimeSpan.FromHours(1) });
        await bus.Publisher.CancelAsync(scheduled);

        bus.Time!.Advance(TimeSpan.FromDays(2));
        var early = await bus.Janitor.RunAsync();
        Assert.True(early.Ran);
        Assert.Equal(1, early.Purged);
        Assert.Single(await bus.FailedOfAsync());

        bus.Time.Advance(TimeSpan.FromDays(2));
        var late = await bus.Janitor.RunAsync();
        Assert.Equal(2, late.Purged);
        Assert.Empty(await bus.FailedOfAsync());
    }

    [Fact]
    public async Task Messages_the_platform_gave_up_on_become_failed_messages()
    {
        await using var bus = new TestBus(Billing);
        await bus.Publisher.PublishAsync(new Order("o1"));
        var lane = bus.Topology.GetSubscriber("billing").Lanes[0];

        // A handler that dies mid-message: the lock runs out ten times and the transport dead-letters it.
        for (var i = 0; i < 10; i++)
        {
            Assert.Single(await bus.Transport.ReceiveAsync(lane, 1, default));
            bus.Time!.Advance(TimeSpan.FromMinutes(6));
        }
        Assert.Empty(await bus.Transport.ReceiveAsync(lane, 1, default));
        Assert.Single(bus.Transport.PeekDeadLetters("billing"));

        var report = await bus.Janitor.RunAsync();

        Assert.Equal(1, report.Rescued);
        Assert.Empty(bus.Transport.PeekDeadLetters("billing"));
        Assert.Contains("gave up", Assert.Single(await bus.FailedOfAsync()).Error);
    }

    [Fact]
    public async Task One_janitor_at_a_time()
    {
        await using var bus = new TestBus(Billing);

        await using (await bus.Store.TryLockAsync("janitor", default))
            Assert.False((await bus.Janitor.RunAsync()).Ran);

        Assert.True((await bus.Janitor.RunAsync()).Ran);
    }
}
