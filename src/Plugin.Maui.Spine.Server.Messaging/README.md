# Plugin.Maui.Spine.Server.Messaging

Queues for a Spine app's server. Publish a typed message, and handle it in an Azure Functions trigger or in a pump in your ASP.NET Core host. The framework does the plumbing:
- **Large payloads** are gzipped, and go to blob storage when still too large.
- **Routing** sends a message to every subscriber that takes it, filtered on type and metadata.
- **Priority** comes in three lanes: high, normal and low.
- **Scheduling** keeps a message dormant until it is due, and lets you cancel it until then.
- **Retries** back off, and a message that runs out of attempts is kept as failed, ready to replay.
- **Cleanup** removes old payloads, markers and failed messages.

This package holds the abstractions and an in-memory transport. A real transport comes from one of these:
- [`.AzureStorage`](https://www.nuget.org/packages/Plugin.Maui.Spine.Server.Messaging.AzureStorage): Storage Queues, simple and cheap.
- [`.ServiceBus`](https://www.nuget.org/packages/Plugin.Maui.Spine.Server.Messaging.ServiceBus): Azure Service Bus, with more features at more cost.

```bash
dotnet add package Plugin.Maui.Spine.Server.Messaging.AzureStorage
```

```csharp
builder.Services.AddSpineMessaging(m =>
{
    m.UseAzureStorage();                                   // ConnectionStrings:queues and :blobs, as Aspire sets them
    m.Message<ResizeImage>("resize-image");
    m.Subscriber("work", s => s.Handles<ResizeImage>().Prioritized().Retry(5));
});
builder.Services.AddMessageHandler<ResizeImage, ResizeImageHandler>();

await publisher.PublishAsync(new ResizeImage("a.png", picture, 800), new PublishOptions { Priority = MessagePriority.High });
```

## Documentation

- [Messaging](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/messaging.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
