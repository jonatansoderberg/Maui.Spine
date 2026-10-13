using System.Security.Cryptography;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Server.Messaging.ServiceBus;
using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

/// <summary>A fact that runs only when the Service Bus emulator (AMQP and management) and Azurite answer.</summary>
public sealed class ServiceBusEmulatorFactAttribute : FactAttribute
{
    // The emulator's published development key; it opens nothing outside a local container.
    public const string ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private static readonly bool Up = AzuriteFactAttribute.Listening(5672) && AzuriteFactAttribute.Listening(5300)
        && AzuriteFactAttribute.Listening(10000);

    public ServiceBusEmulatorFactAttribute()
    {
        if (!Up)
            Skip = "No Service Bus emulator on localhost:5672 and 5300, or no Azurite on 10000. See docs/wiki/messaging.md, Testing.";
    }
}

[Collection("Emulators")]
public sealed class ServiceBusIntegrationTests : IAsyncLifetime
{
    private readonly string _topic = $"spine-test-{Guid.NewGuid():N}"[..24];
    private readonly string _container = $"spine-test-{Guid.NewGuid():N}"[..30];
    private readonly Journal _journal = new();
    private ServiceProvider _services = null!;

    private void Build(int maxDeliveryCount = 10)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:servicebus"] = ServiceBusEmulatorFactAttribute.ConnectionString,
            ["ConnectionStrings:blobs"] = AzuriteFactAttribute.ConnectionString,
        }).Build());
        services.AddSingleton(_journal);
        services.AddSpineMessaging(m =>
        {
            m.UseServiceBus(configure: o =>
            {
                o.TopicName = _topic;
                o.Container = _container;
                o.MaxDeliveryCount = maxDeliveryCount;
                o.ReceiveWait = TimeSpan.FromSeconds(1);
            });
            m.Message<Order>("order");
            m.Message<Big>("big");
            m.Subscriber("billing", s => s.Handles<Order>().Prioritized().Retry(3, first: TimeSpan.FromSeconds(1)));
            m.Subscriber("north", s => s.Handles<Order>().Where("region", "north"));
            m.Subscriber("archive", s => s.Handles<Big>());
        });
        services.AddMessageHandler<Order, OrderHandler>();
        services.AddMessageHandler<Big, BigHandler>();
        _services = services.BuildServiceProvider();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
        if (AzuriteFactAttribute.Listening(5300))
        {
            var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorFactAttribute.ConnectionString.Replace("sb://localhost", "sb://localhost:5300"));
            if (await admin.TopicExistsAsync(_topic))
                await admin.DeleteTopicAsync(_topic);
            await new BlobContainerClient(AzuriteFactAttribute.ConnectionString, _container).DeleteIfExistsAsync();
        }
    }

    private IMessagePublisher Publisher => _services.GetRequiredService<IMessagePublisher>();
    private ServiceBusTransport Transport => _services.GetRequiredService<ServiceBusTransport>();
    private MessageTopology Topology => _services.GetRequiredService<MessageTopology>();

    private async Task<int> DrainAsync(string subscriber, bool abandon = false)
    {
        var count = 0;
        foreach (var lane in Topology.GetSubscriber(subscriber).Lanes)
        {
            while (await Transport.ReceiveAsync(lane, 32, wait: true, default) is { Count: > 0 } batch)
            {
                foreach (var delivery in batch)
                {
                    count++;
                    if (abandon)
                    {
                        await delivery.AbandonAsync(default);
                        continue;
                    }
                    await _services.GetRequiredService<MessageDispatcher>().DispatchAsync(subscriber, delivery.Envelope, delivery.DeliveryCount, default);
                    await delivery.CompleteAsync(default);
                }
                if (abandon)
                    return count;
            }
        }
        return count;
    }

    [ServiceBusEmulatorFact]
    public async Task Provisioning_makes_a_subscription_per_lane_with_correlation_rules_and_no_default()
    {
        Build();
        await Publisher.PublishAsync(new Order("o1"));

        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorFactAttribute.ConnectionString.Replace("sb://localhost", "sb://localhost:5300"));
        var subscriptions = new List<string>();
        await foreach (var subscription in admin.GetSubscriptionsAsync(_topic))
            subscriptions.Add(subscription.SubscriptionName);
        Assert.Equal(["archive", "billing", "billing-high", "billing-low", "north"], subscriptions.Order());

        var rules = new List<RuleProperties>();
        await foreach (var rule in admin.GetRulesAsync(_topic, "north"))
            rules.Add(rule);
        Assert.Equal(["direct", "type-order"], rules.Select(r => r.Name).Order());
        var typeRule = (CorrelationRuleFilter)rules.Single(r => r.Name == "type-order").Filter;
        Assert.Equal("order", typeRule.Subject);
        Assert.Equal("north", typeRule.ApplicationProperties["region"]);
    }

    [ServiceBusEmulatorFact]
    public async Task Service_Bus_routes_by_type_metadata_and_priority()
    {
        Build();

        await Publisher.PublishAsync(new Order("north-high"), new PublishOptions { Priority = MessagePriority.High, Metadata = { ["region"] = "north" } });
        await Publisher.PublishAsync(new Order("south-low"), new PublishOptions { Priority = MessagePriority.Low, Metadata = { ["region"] = "south" } });

        Assert.Equal(2, await DrainAsync("billing"));
        Assert.Equal(1, await DrainAsync("north"));
        Assert.Equal(0, await DrainAsync("archive"));

        var billing = _journal.Contexts.Where(c => c.Subscriber == "billing").ToList();
        Assert.Equal([MessagePriority.High, MessagePriority.Low], billing.Select(c => c.Priority));
        Assert.Equal("north", Assert.Single(_journal.Contexts, c => c.Subscriber == "north").Metadata["region"]);
    }

    [ServiceBusEmulatorFact]
    public async Task A_retry_goes_back_to_its_own_subscriber_only()
    {
        Build();
        var order = await Publisher.PublishAsync(new Order("o1"), new PublishOptions { Metadata = { ["region"] = "north" } });
        _journal.Failures[order.MessageId] = 1;

        await DrainAsync("billing");
        await DrainAsync("north");
        await Task.Delay(TimeSpan.FromSeconds(2));
        await DrainAsync("billing");
        Assert.Equal(0, await DrainAsync("north"));

        Assert.Equal([("billing", 2), ("north", 1)], _journal.Contexts.Select(c => (c.Subscriber, c.Attempt)).Order());
    }

    [ServiceBusEmulatorFact]
    public async Task Scheduling_is_native_and_a_large_payload_goes_through_blob_storage()
    {
        Build();
        var text = Convert.ToBase64String(RandomNumberGenerator.GetBytes(300 * 1024));

        await Publisher.PublishAsync(new Big(text), new PublishOptions { Delay = TimeSpan.FromSeconds(3) });

        Assert.Equal(0, await DrainAsync("archive"));
        await Task.Delay(TimeSpan.FromSeconds(4));
        Assert.Equal(1, await DrainAsync("archive"));
        Assert.Equal(text, ((Big)Assert.Single(_journal.Handled).Message).Text);
    }

    [ServiceBusEmulatorFact]
    public async Task The_janitor_moves_dead_lettered_messages_into_the_failed_messages()
    {
        Build(maxDeliveryCount: 2);
        await Publisher.PublishAsync(new Big("crashes"));

        await DrainAsync("archive", abandon: true);
        await DrainAsync("archive", abandon: true);

        var report = await _services.GetRequiredService<MessagingJanitor>().RunAsync();

        Assert.Equal(1, report.Rescued);
        var failed = new List<FailedMessage>();
        await foreach (var message in _services.GetRequiredService<IFailedMessages>().ListAsync("archive"))
            failed.Add(message);
        Assert.Equal("big", Assert.Single(failed).Type);
    }

    [ServiceBusEmulatorFact]
    public async Task Changing_a_filter_updates_the_rules()
    {
        Build();
        await Publisher.PublishAsync(new Order("o1"));
        var configuration = _services.GetRequiredService<IConfiguration>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSpineMessaging(m =>
        {
            m.UseServiceBus(configure: o =>
            {
                o.TopicName = _topic;
                o.Container = _container;
            });
            m.Message<Order>("order");
            m.Subscriber("north", s => s.Handles<Order>().Where("region", "south"));
        });
        await using var changed = services.BuildServiceProvider();
        await changed.GetRequiredService<IMessagePublisher>().PublishAsync(new Order("o2"));

        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorFactAttribute.ConnectionString.Replace("sb://localhost", "sb://localhost:5300"));
        var rule = await admin.GetRuleAsync(_topic, "north", "type-order");
        Assert.Equal("south", ((CorrelationRuleFilter)rule.Value.Filter).ApplicationProperties["region"]);
    }
}
