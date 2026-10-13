using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Server.Messaging.InMemory;
using Plugin.Maui.Spine.Server.Messaging.Transport;
using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class FailureHandlingTests
{
    private static TestBus Flaky(Action<FlakyTransport>? setUp = null, TransportLimits? limits = null) => new(m =>
    {
        m.UseTransport(sp =>
        {
            var flaky = new FlakyTransport(sp.GetRequiredService<InMemoryTransport>());
            setUp?.Invoke(flaky);
            return flaky;
        });
        m.Message<Order>();
        m.Subscriber("billing", s => s.Handles<Order>());
    }, limits);

    [Fact]
    public async Task A_publish_that_fails_gives_its_deduplication_id_back()
    {
        await using var bus = Flaky(t => t.FailSends = 1);
        var options = new PublishOptions { DeduplicationId = "order-o1" };

        await Assert.ThrowsAsync<IOException>(() => bus.Publisher.PublishAsync(new Order("o1"), options));
        var retry = await bus.Publisher.PublishAsync(new Order("o1"), options);

        Assert.False(retry.IsDuplicate);
        Assert.Single(bus.Transport.Peek("billing"));
    }

    [Fact]
    public async Task Messaging_carries_on_when_it_may_not_provision()
    {
        FlakyTransport? transport = null;
        await using var bus = Flaky(t =>
        {
            t.FailProvisioning = true;
            transport = t;
        });
        await bus.Services.GetRequiredService<InMemoryTransport>().ProvisionAsync(bus.Topology, default);

        await bus.Publisher.PublishAsync(new Order("o1"));
        await bus.Publisher.PublishAsync(new Order("o2"));

        Assert.Equal(2, await bus.DrainAsync("billing"));
        Assert.Equal(2, bus.Journal.Handled.Count);
        Assert.Equal(1, transport!.ProvisionAttempts);

        bus.Time!.Advance(TimeSpan.FromSeconds(31));
        transport.FailProvisioning = false;
        await bus.Publisher.PublishAsync(new Order("o3"));
        await bus.Publisher.PublishAsync(new Order("o4"));
        Assert.Equal(2, transport.ProvisionAttempts);
    }

    [Fact]
    public async Task Metadata_counts_against_what_travels_inline()
    {
        await using var bus = new TestBus(m =>
        {
            m.Message<Big>();
            m.Subscriber("archive", s => s.Handles<Big>());
        }, new TransportLimits(4 * 1024, TimeSpan.MaxValue, Base64Body: true));
        var text = new string('x', 2 * 1024);

        await bus.Publisher.PublishAsync(new Big(text));
        await bus.Publisher.PublishAsync(new Big(text), new PublishOptions { Metadata = { ["note"] = new string('m', 3 * 1024) } });

        var sent = bus.Transport.Peek("archive");
        Assert.Null(sent[0].Payload);
        Assert.NotNull(sent[1].Payload);
    }

    [Fact]
    public async Task Metadata_has_a_ceiling()
    {
        await using var bus = Flaky();

        var tooMuch = new PublishOptions { Metadata = { ["a"] = new string('x', 3000), ["b"] = new string('y', 2000) } };

        await Assert.ThrowsAsync<ArgumentException>(() => bus.Publisher.PublishAsync(new Order("o1"), tooMuch));
    }

    [Fact]
    public void A_key_filtered_twice_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSpineMessaging(m =>
        {
            m.UseInMemory();
            m.Message<Order>();
            m.Subscriber("stock", s => s.Handles<Order>().Where("region", "north").Where("region", "south"));
        }));
    }
}
