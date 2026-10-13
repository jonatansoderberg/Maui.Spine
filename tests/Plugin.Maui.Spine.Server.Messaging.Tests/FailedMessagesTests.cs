using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class FailedMessagesTests
{
    private static void Shop(MessagingBuilder m)
    {
        m.Message<Order>();
        m.Message<Mail>();
        m.Subscriber("billing", s => s.Handles<Order>().Retry(1).Prioritized());
        m.Subscriber("mailer", s => s.Handles<Mail>().Retry(1));
    }

    [Fact]
    public async Task Failed_messages_are_listed_per_subscriber()
    {
        await using var bus = new TestBus(Shop);
        var order = await bus.Publisher.PublishAsync(new Order("o1"));
        var mail = await bus.Publisher.PublishAsync(new Mail("a@b"));
        bus.Journal.Failures[order.MessageId] = -1;
        bus.Journal.Failures[mail.MessageId] = -1;

        await bus.DrainAsync("billing");
        await bus.DrainAsync("mailer");

        Assert.Equal(2, (await bus.FailedOfAsync()).Count);
        Assert.Equal(order.MessageId, Assert.Single(await bus.FailedOfAsync("billing")).MessageId);
    }

    [Fact]
    public async Task A_replay_goes_to_its_subscriber_alone_from_attempt_one()
    {
        await using var bus = new TestBus(m =>
        {
            m.Message<Order>();
            m.Subscriber("billing", s => s.Handles<Order>().Retry(1).Prioritized());
            m.Subscriber("stock", s => s.Handles<Order>());
        });
        var order = await bus.Publisher.PublishAsync(new Order("o1"), new PublishOptions { Priority = MessagePriority.High });
        bus.Journal.Failures[order.MessageId] = 1;
        await bus.DrainAsync("billing");
        await bus.DrainAsync("stock");

        var failed = Assert.Single(await bus.FailedOfAsync());
        await bus.Failed.ReplayAsync(failed);

        Assert.Empty(await bus.FailedOfAsync());
        Assert.Single(bus.Transport.Peek("billing-high"));
        Assert.Empty(bus.Transport.Peek("stock"));
        await bus.DrainAsync("billing");
        Assert.Equal(1, bus.Journal.Contexts.Single(c => c.Subscriber == "billing").Attempt);
    }

    [Fact]
    public async Task A_failed_message_can_be_deleted()
    {
        await using var bus = new TestBus(Shop);
        var mail = await bus.Publisher.PublishAsync(new Mail("a@b"));
        bus.Journal.Failures[mail.MessageId] = -1;
        await bus.DrainAsync("mailer");

        await bus.Failed.DeleteAsync(Assert.Single(await bus.FailedOfAsync()));

        Assert.Empty(await bus.FailedOfAsync());
    }
}
