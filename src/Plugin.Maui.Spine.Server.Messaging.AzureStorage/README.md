# Plugin.Maui.Spine.Server.Messaging.AzureStorage

Azure Storage for [Spine messaging](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/messaging.md):
- **Storage Queues as the transport:** one queue per subscriber and lane, paid per operation with no base charge.
- **A blob container as the store:** large payloads, failed messages and markers. Every transport uses it, Service Bus too.

Under Aspire it runs on Azurite.

```bash
dotnet add package Plugin.Maui.Spine.Server.Messaging.AzureStorage
```

```csharp
builder.Services.AddSpineMessaging(m =>
{
    m.UseAzureStorage();   // ConnectionStrings:queues and ConnectionStrings:blobs
    // message types and subscribers
});
```

```csharp
// Azure Functions: one trigger per lane.
[Function("Work")]
public Task Work([QueueTrigger("work", Connection = "queues")] QueueMessage message, CancellationToken ct)
    => dispatcher.DispatchAsync("work", message, ct);
```

## Documentation

- [Messaging](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/messaging.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
