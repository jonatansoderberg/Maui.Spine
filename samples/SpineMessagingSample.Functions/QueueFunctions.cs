using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Plugin.Maui.Spine.Server.Messaging;
using Plugin.Maui.Spine.Server.Messaging.AzureStorage;
using Plugin.Maui.Spine.Server.Messaging.ServiceBus;

namespace SpineMessagingSample.Functions;

/// <summary>
/// The "work" subscriber's three lanes, for each transport. Each lane is its own function, so a high-priority picture
/// never waits behind a backlog of normal ones. The AppHost disables the set for the transport it does not run.
/// </summary>
public sealed class QueueFunctions(MessageDispatcher dispatcher)
{
    private const string Topic = "spine-messages";

    [Function("WorkHighQueue")]
    public Task WorkHighQueue([QueueTrigger("work-high", Connection = "queues")] QueueMessage message, CancellationToken ct) =>
        dispatcher.DispatchAsync("work", message, ct);

    [Function("WorkQueue")]
    public Task WorkQueue([QueueTrigger("work", Connection = "queues")] QueueMessage message, CancellationToken ct) =>
        dispatcher.DispatchAsync("work", message, ct);

    [Function("WorkLowQueue")]
    public Task WorkLowQueue([QueueTrigger("work-low", Connection = "queues")] QueueMessage message, CancellationToken ct) =>
        dispatcher.DispatchAsync("work", message, ct);

    [Function("WorkHighServiceBus")]
    public Task WorkHighServiceBus([ServiceBusTrigger(Topic, "work-high", Connection = "servicebus")] ServiceBusReceivedMessage message, CancellationToken ct) =>
        dispatcher.DispatchAsync("work", message, ct);

    [Function("WorkServiceBus")]
    public Task WorkServiceBus([ServiceBusTrigger(Topic, "work", Connection = "servicebus")] ServiceBusReceivedMessage message, CancellationToken ct) =>
        dispatcher.DispatchAsync("work", message, ct);

    [Function("WorkLowServiceBus")]
    public Task WorkLowServiceBus([ServiceBusTrigger(Topic, "work-low", Connection = "servicebus")] ServiceBusReceivedMessage message, CancellationToken ct) =>
        dispatcher.DispatchAsync("work", message, ct);
}
