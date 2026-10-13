# Plugin.Maui.Spine.Server.Messaging.ServiceBus

Azure Service Bus for [Spine messaging](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/messaging.md):
- **One topic,** with a subscription per subscriber and lane.
- **Correlation filters route** each message to the subscribers that take it.
- **Scheduling is native,** with no limit.
- **Payloads up to 192 KB travel inline.** Larger ones, and failed messages, go to blob storage.
- **Provisioning:** the host creates the topic, the subscriptions and their rules at startup.

Under Aspire it runs on the Service Bus emulator.

```bash
dotnet add package Plugin.Maui.Spine.Server.Messaging.ServiceBus
```

```csharp
builder.Services.AddSpineMessaging(m =>
{
    m.UseServiceBus();   // ConnectionStrings:servicebus and ConnectionStrings:blobs
    // message types and subscribers
});
```

```csharp
// Azure Functions: one trigger per lane.
[Function("Work")]
public Task Work([ServiceBusTrigger("spine-messages", "work", Connection = "servicebus")] ServiceBusReceivedMessage message, CancellationToken ct)
    => dispatcher.DispatchAsync("work", message, ct);
```

## Documentation

- [Messaging](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/messaging.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
