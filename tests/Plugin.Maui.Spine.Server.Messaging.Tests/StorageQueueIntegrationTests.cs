using System.Net.Sockets;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Server.Messaging.AzureStorage;
using Plugin.Maui.Spine.Server.Messaging.Transport;
using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

/// <summary>A fact that runs only when Azurite answers on its queue and blob ports.</summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    public const string ConnectionString = "UseDevelopmentStorage=true";

    private static readonly bool Up = Listening(10000) && Listening(10001);

    public AzuriteFactAttribute()
    {
        if (!Up)
            Skip = "No Azurite on 127.0.0.1:10000–10001. Run `docker run -p 10000-10002:10000-10002 mcr.microsoft.com/azure-storage/azurite` or an Aspire AppHost with storage.";
    }

    internal static bool Listening(int port)
    {
        try
        {
            using var socket = new TcpClient();
            return socket.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromSeconds(1)) && socket.Connected;
        }
        catch
        {
            return false;
        }
    }
}

[Collection("Emulators")]
public sealed class StorageQueueIntegrationTests : IAsyncLifetime
{
    private readonly string _prefix = $"t{Guid.NewGuid():N}"[..9] + "-";
    private readonly string _container = $"spine-test-{Guid.NewGuid():N}"[..30];
    private ServiceProvider _services = null!;
    private readonly Journal _journal = new();

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:queues"] = AzuriteFactAttribute.ConnectionString,
            ["ConnectionStrings:blobs"] = AzuriteFactAttribute.ConnectionString,
        }).Build());
        services.AddSingleton(_journal);
        services.AddSpineMessaging(m =>
        {
            m.UseAzureStorage(configure: o =>
            {
                o.QueuePrefix = _prefix;
                o.Container = _container;
            });
            m.Message<Order>("order");
            m.Message<Big>("big");
            m.Subscriber("billing", s => s.Handles<Order>().Prioritized().Retry(2, first: TimeSpan.FromSeconds(1)));
            m.Subscriber("archive", s => s.Handles<Big>());
        });
        services.AddMessageHandler<Order, OrderHandler>();
        services.AddMessageHandler<Big, BigHandler>();
        _services = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (AzuriteFactAttribute.Listening(10000))
        {
            var queues = new QueueServiceClient(AzuriteFactAttribute.ConnectionString);
            await foreach (var queue in queues.GetQueuesAsync(prefix: _prefix))
                await queues.DeleteQueueAsync(queue.Name);
            await new BlobContainerClient(AzuriteFactAttribute.ConnectionString, _container).DeleteIfExistsAsync();
        }
        await _services.DisposeAsync();
    }

    private IMessagePublisher Publisher => _services.GetRequiredService<IMessagePublisher>();
    private StorageQueueTransport Transport => _services.GetRequiredService<StorageQueueTransport>();
    private MessageTopology Topology => _services.GetRequiredService<MessageTopology>();

    private async Task<int> DrainAsync(string subscriber)
    {
        var count = 0;
        foreach (var lane in Topology.GetSubscriber(subscriber).Lanes)
        {
            while (await Transport.ReceiveAsync(lane, 32, wait: true, default) is { Count: > 0 } batch)
            {
                foreach (var delivery in batch)
                {
                    await _services.GetRequiredService<MessageDispatcher>().DispatchAsync(subscriber, delivery.Envelope, delivery.DeliveryCount, default);
                    await delivery.CompleteAsync(default);
                    count++;
                }
            }
        }
        return count;
    }

    [AzuriteFact]
    public async Task Provisioning_creates_a_queue_per_lane()
    {
        await Publisher.PublishAsync(new Order("o1"));

        var names = new List<string>();
        await foreach (var queue in new QueueServiceClient(AzuriteFactAttribute.ConnectionString).GetQueuesAsync(prefix: _prefix))
            names.Add(queue.Name[_prefix.Length..]);
        Assert.Equal(["archive", "billing", "billing-high", "billing-low"], names.Order());
    }

    [AzuriteFact]
    public async Task A_message_round_trips_with_its_metadata_and_priority()
    {
        var result = await Publisher.PublishAsync(new Order("o1", 3), new PublishOptions
        {
            Priority = MessagePriority.High,
            Metadata = { ["region"] = "north" },
            CorrelationId = "flow",
        });

        Assert.Equal(1, await DrainAsync("billing"));

        var (_, message, context) = Assert.Single(_journal.Handled);
        Assert.Equal(new Order("o1", 3), message);
        Assert.Equal(result.MessageId, context.MessageId);
        Assert.Equal(MessagePriority.High, context.Priority);
        Assert.Equal("north", context.Metadata["region"]);
        Assert.Equal("flow", context.CorrelationId);
    }

    [AzuriteFact]
    public async Task A_large_payload_goes_through_blob_storage_and_is_deleted_after()
    {
        var text = Convert.ToBase64String(RandomNumberGenerator.GetBytes(120 * 1024));

        await Publisher.PublishAsync(new Big(text));

        var container = new BlobContainerClient(AzuriteFactAttribute.ConnectionString, _container);
        Assert.Single(await Blobs(container, "payloads/"));

        Assert.Equal(1, await DrainAsync("archive"));
        Assert.Equal(text, ((Big)Assert.Single(_journal.Handled).Message).Text);
        Assert.Empty(await Blobs(container, "payloads/"));
    }

    [AzuriteFact]
    public async Task A_scheduled_message_is_invisible_until_due()
    {
        await Publisher.PublishAsync(new Order("o1"), new PublishOptions { Delay = TimeSpan.FromSeconds(3) });

        Assert.Equal(0, await DrainAsync("billing"));
        await Task.Delay(TimeSpan.FromSeconds(4));
        Assert.Equal(1, await DrainAsync("billing"));
        Assert.Single(_journal.Handled);
    }

    [AzuriteFact]
    public async Task Retries_failures_cancels_and_deduplication_use_blob_storage()
    {
        var failing = await Publisher.PublishAsync(new Order("bad"));
        _journal.Failures[failing.MessageId] = -1;
        var scheduled = await Publisher.PublishAsync(new Order("later"), new PublishOptions { Delay = TimeSpan.FromSeconds(2) });
        await Publisher.CancelAsync(scheduled);
        var first = await Publisher.PublishAsync(new Order("d"), new PublishOptions { DeduplicationId = "order-d" });
        var repeat = await Publisher.PublishAsync(new Order("d"), new PublishOptions { DeduplicationId = "order-d" });

        await DrainAsync("billing");
        await Task.Delay(TimeSpan.FromSeconds(3));
        await DrainAsync("billing");

        Assert.False(first.IsDuplicate);
        Assert.True(repeat.IsDuplicate);
        Assert.Equal(["d"], _journal.Handled.Select(h => ((Order)h.Message).Id));

        var failed = new List<FailedMessage>();
        await foreach (var message in _services.GetRequiredService<IFailedMessages>().ListAsync("billing"))
            failed.Add(message);
        Assert.Equal(failing.MessageId, Assert.Single(failed).MessageId);
        Assert.Equal(2, failed[0].Attempts);
    }

    [AzuriteFact]
    public async Task The_janitor_holds_a_lease_and_purges_by_day()
    {
        var store = _services.GetRequiredService<BlobMessageStore>();
        await store.ProvisionAsync(default);
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        await store.PutPayloadAsync("p-old", new byte[] { 1 }, old, default);
        await store.PutPayloadAsync("p-new", new byte[] { 2 }, DateTimeOffset.UtcNow.AddDays(3), default);
        await store.CancelAsync("c-old", old, default);
        await store.SaveFailedAsync(new FailedMessage("billing", new MessageEnvelope { Id = "f-old", Type = "order", Body = "{}"u8.ToArray() }, old, "x", null), default);
        await store.TryReserveAsync("d-old", old, default);

        await using (var held = await store.TryLockAsync("janitor", default))
        {
            Assert.NotNull(held);
            Assert.Null(await store.TryLockAsync("janitor", default));
        }

        var removed = await store.PurgeAsync(DateTimeOffset.UtcNow, TimeSpan.FromDays(14), default);

        Assert.Equal(4, removed);
        var container = new BlobContainerClient(AzuriteFactAttribute.ConnectionString, _container);
        Assert.Equal(["p-new"], (await Blobs(container, "payloads/")).Select(n => n[(n.LastIndexOf('/') + 1)..]));
    }

    private static async Task<List<string>> Blobs(BlobContainerClient container, string prefix)
    {
        var names = new List<string>();
        await foreach (var item in container.GetBlobsAsync(new Azure.Storage.Blobs.Models.GetBlobsOptions { Prefix = prefix }))
            names.Add(item.Name);
        return names;
    }
}
