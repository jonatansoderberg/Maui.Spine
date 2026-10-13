using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plugin.Maui.Spine.Server.Messaging.AzureStorage;
using Plugin.Maui.Spine.Server.Messaging.InMemory;
using Plugin.Maui.Spine.Server.Messaging.ServiceBus;
using Xunit;
using Xunit.Abstractions;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

/// <summary>A fact that runs only when <c>SPINE_MESSAGING_THROUGHPUT=1</c>, against the local emulators.</summary>
public sealed class ThroughputFactAttribute : FactAttribute
{
    public ThroughputFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SPINE_MESSAGING_THROUGHPUT") != "1")
            Skip = "Set SPINE_MESSAGING_THROUGHPUT=1, with Azurite and the Service Bus emulator running, to measure throughput.";
    }
}

/// <summary>
/// Publishes 10,000 small messages in batches and handles them with one pump of 32 handlers, timing both. The numbers
/// are for the local emulators on one machine, which are slower than the cloud services; they show the shape, not the
/// ceiling.
/// </summary>
[Collection("Emulators")]
public sealed class ThroughputTests(ITestOutputHelper output)
{
    private static readonly int Messages = int.TryParse(Environment.GetEnvironmentVariable("SPINE_MESSAGING_THROUGHPUT_COUNT"), out var count) ? count : 10_000;

    [ThroughputFact]
    public Task In_memory() => MeasureAsync("In memory", m => m.UseInMemory());

    [ThroughputFact]
    public Task Storage_queues() => MeasureAsync("Storage Queues on Azurite", m => m.UseAzureStorage(configure: o =>
    {
        o.QueuePrefix = $"t{Guid.NewGuid():N}"[..9] + "-";
        o.Container = $"spine-test-{Guid.NewGuid():N}"[..30];
    }));

    [ThroughputFact]
    public Task Service_Bus() => MeasureAsync("Service Bus on its emulator", m => m.UseServiceBus(configure: o =>
    {
        o.TopicName = $"spine-test-{Guid.NewGuid():N}"[..24];
        o.Container = $"spine-test-{Guid.NewGuid():N}"[..30];
    }));

    private async Task MeasureAsync(string name, Action<MessagingBuilder> transport)
    {
        var journal = new Journal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:queues"] = AzuriteFactAttribute.ConnectionString,
            ["ConnectionStrings:blobs"] = AzuriteFactAttribute.ConnectionString,
            ["ConnectionStrings:servicebus"] = ServiceBusEmulatorFactAttribute.ConnectionString,
        }).Build());
        services.AddSingleton(journal);
        services.AddSpineMessaging(m =>
        {
            transport(m);
            m.Message<Order>("order");
            m.Subscriber("billing", s => s.Handles<Order>().MaxConcurrency(32));
        });
        services.AddMessageHandler<Order, OrderHandler>();
        services.AddSpineMessagePump("billing");
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher>();
        await publisher.PublishAsync(new Order("warm-up"));

        var publishing = Stopwatch.StartNew();
        foreach (var chunk in Enumerable.Range(0, Messages).Chunk(500))
            await publisher.PublishBatchAsync(chunk.Select(i => new Order($"o{i}", i)));
        publishing.Stop();

        var pump = provider.GetServices<IHostedService>().Single(s => s.GetType().Name == "MessagePump");
        var handling = Stopwatch.StartNew();
        await pump.StartAsync(default);
        while (journal.Handled.Count < Messages + 1 && handling.Elapsed < TimeSpan.FromMinutes(10))
            await Task.Delay(200);
        handling.Stop();
        await pump.StopAsync(default);

        output.WriteLine($"{name}: published {Messages} in {publishing.Elapsed.TotalSeconds:N1} s ({Messages / publishing.Elapsed.TotalSeconds:N0}/s), " +
            $"handled {journal.Handled.Count - 1} in {handling.Elapsed.TotalSeconds:N1} s ({(journal.Handled.Count - 1) / handling.Elapsed.TotalSeconds:N0}/s).");
        Assert.Equal(Messages + 1, journal.Handled.Count);
    }
}
