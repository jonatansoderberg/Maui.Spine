using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Server.Messaging.InMemory;
using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class RoutingTests
{
    private static void Shop(MessagingBuilder m)
    {
        m.Message<Order>("order");
        m.Message<Mail>("mail");
        m.Subscriber("billing", s => s.Handles<Order>());
        m.Subscriber("north-stock", s => s.Handles<Order>().Where("region", "north"));
        m.Subscriber("mailer", s => s.Handles<Mail>());
    }

    [Fact]
    public async Task A_message_goes_to_every_subscriber_that_takes_it()
    {
        await using var bus = new TestBus(Shop);

        var result = await bus.Publisher.PublishAsync(new Order("o1"), new PublishOptions { Metadata = { ["region"] = "north" } });

        Assert.Equal(["billing", "north-stock"], result.Subscribers);
        Assert.Equal(1, await bus.DrainAsync("billing"));
        Assert.Equal(1, await bus.DrainAsync("north-stock"));
        Assert.Equal(0, await bus.DrainAsync("mailer"));
        Assert.All(bus.Journal.Contexts, c => Assert.Equal(result.MessageId, c.MessageId));
    }

    [Fact]
    public async Task Metadata_filters_must_all_match()
    {
        await using var bus = new TestBus(Shop);

        var south = await bus.Publisher.PublishAsync(new Order("o1"), new PublishOptions { Metadata = { ["region"] = "south" } });
        var none = await bus.Publisher.PublishAsync(new Order("o2"));

        Assert.Equal(["billing"], south.Subscribers);
        Assert.Equal(["billing"], none.Subscribers);
        Assert.Empty(bus.Transport.Peek("north-stock"));
    }

    [Fact]
    public async Task A_message_no_one_takes_is_not_sent()
    {
        await using var bus = new TestBus(m =>
        {
            m.Message<Order>();
            m.Message<Mail>();
            m.Subscriber("billing", s => s.Handles<Order>());
        });

        var result = await bus.Publisher.PublishAsync(new Mail("a@b"));

        Assert.Empty(result.Subscribers);
        Assert.Empty(bus.Transport.Peek("billing"));
    }

    [Fact]
    public async Task The_handler_gets_the_payload_and_its_context()
    {
        await using var bus = new TestBus(Shop);

        await bus.Publisher.PublishAsync(new Mail("a@b", "Hej"), new PublishOptions { CorrelationId = "flow-1", Metadata = { ["lang"] = "sv" } });
        await bus.DrainAsync("mailer");

        var (subscriber, message, context) = Assert.Single(bus.Journal.Handled);
        Assert.Equal("mailer", subscriber);
        Assert.Equal(new Mail("a@b", "Hej"), message);
        Assert.Equal("mail", context.Type);
        Assert.Equal("flow-1", context.CorrelationId);
        Assert.Equal("sv", context.Metadata["lang"]);
        Assert.Equal(1, context.Attempt);
        Assert.Equal(TestBus.Start, context.EnqueuedAt);
    }

    [Fact]
    public async Task An_unregistered_type_or_a_reserved_metadata_key_is_refused()
    {
        await using var bus = new TestBus(Shop);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bus.Publisher.PublishAsync(new Big("x")));
        await Assert.ThrowsAsync<ArgumentException>(() => bus.Publisher.PublishAsync(new Order("o"), new PublishOptions { Metadata = { ["spine-to"] = "x" } }));
    }

    [Theory]
    [InlineData("Billing")]
    [InlineData("ab")]
    [InlineData("a--b")]
    [InlineData("-ab")]
    public void A_subscriber_name_must_suit_every_transport(string name)
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddSpineMessaging(m =>
        {
            m.UseInMemory();
            m.Message<Order>();
            m.Subscriber(name, s => s.Handles<Order>());
        }));
    }

    [Fact]
    public void Declarations_that_cannot_work_are_refused_at_startup()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSpineMessaging(m =>
        {
            m.Message<Order>();
            m.Subscriber("billing", s => s.Handles<Order>());
        }));

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSpineMessaging(m =>
        {
            m.UseInMemory();
            m.Subscriber("billing", s => s.Handles<Order>());
        }));

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSpineMessaging(m =>
        {
            m.UseInMemory();
            m.Message<Order>();
            m.Subscriber("billing", s => s.Handles<Order>().Prioritized());
            m.Subscriber("billing-high", s => s.Handles<Order>());
        }));

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSpineMessaging(m =>
        {
            m.UseInMemory();
            m.Message<Order>("same");
            m.Message<Mail>("same");
        }));
    }

    [Fact]
    public async Task A_host_without_the_handler_keeps_the_message_as_failed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSpineMessaging(m =>
        {
            m.UseInMemory();
            m.Message<Order>();
            m.Subscriber("billing", s => s.Handles<Order>());
        });
        await using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<InMemoryTransport>();

        await provider.GetRequiredService<IMessagePublisher>().PublishAsync(new Order("o1"));
        var delivery = Assert.Single(await transport.ReceiveAsync(new MessageLane("billing", MessagePriority.Normal, "billing"), 1, wait: false, default));
        await provider.GetRequiredService<MessageDispatcher>().DispatchAsync("billing", delivery.Envelope, 1, default);

        var failed = new List<FailedMessage>();
        await foreach (var message in provider.GetRequiredService<IFailedMessages>().ListAsync())
            failed.Add(message);
        Assert.Contains("No IMessageHandler<Order>", Assert.Single(failed).Error);
    }
}
