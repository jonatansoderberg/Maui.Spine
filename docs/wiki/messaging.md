# Messaging

```bash
dotnet add package Plugin.Maui.Spine.Server.Messaging.AzureStorage   # or .ServiceBus
```

Server work that is slow, expensive or can wait goes on a queue: a generated image, a mail, an import, a call to another service. One or more Azure Functions, or a pump in the ASP.NET Core host, handles it. Spine messaging gives the app a typed publisher and typed handlers, and does the rest:

- **Large payloads** are gzipped, and go to blob storage when they are still too large for the queue.
- **Routing:** a message goes to every subscriber that takes its type and metadata.
- **Priority:** a subscriber can have a high, a normal and a low lane.
- **Scheduling:** a message stays dormant until it is due, and can be cancelled until then.
- **Retries** back off. A message that runs out of attempts is kept as failed, ready to replay.
- **Cleanup:** old payloads, markers and failed messages are removed.
- **Traces:** one trace from publish to handler, in the Aspire dashboard.

The same declaration runs on three transports:

| Package | Transport | For |
|---|---|---|
| `Plugin.Maui.Spine.Server.Messaging` | In memory, plus the abstractions | Tests, and one host that may lose its queue on restart |
| `Plugin.Maui.Spine.Server.Messaging.AzureStorage` | Azure Storage Queues | Most apps: simple, cheap, paid per operation |
| `Plugin.Maui.Spine.Server.Messaging.ServiceBus` | Azure Service Bus | Larger payloads inline, schedules beyond 7 days without hops, broker-side routing, faster delivery |

Every transport keeps its state in blob storage: large payloads, failed messages and markers. An Azure Functions app always has a storage account, so this costs nothing extra.

---

## Setup

Declare messaging once, in a method the publishing host and every handling host share (a class library, as in the sample). Storage Queues route on the publishing side, so the publisher must know the subscribers.

```csharp
public static IServiceCollection AddAppMessaging(this IServiceCollection services)
{
    services.AddSpineMessaging(m =>
    {
        m.UseAzureStorage();                                   // ConnectionStrings:queues and ConnectionStrings:blobs

        m.Message<ResizeImage>("resize-image");                // a stable name on the wire
        m.Message<SendReceipt>("send-receipt", AppJson.Default.SendReceipt);   // optional source-generated JSON

        m.Subscriber("work", s => s
            .Handles<ResizeImage>()
            .Where("region", "north")                          // metadata filter; several must all match
            .Prioritized()                                     // work-high, work, work-low
            .MaxConcurrency(8)                                 // per pump
            .Retry(5, first: TimeSpan.FromSeconds(10), max: TimeSpan.FromMinutes(10)));

        m.Subscriber("receipts", s => s.Handles<SendReceipt>());

        m.Retention = TimeSpan.FromDays(14);
    });

    services.AddMessageHandler<ResizeImage, ResizeImageHandler>();
    services.AddMessageHandler<SendReceipt, SendReceiptHandler>();
    return services;
}
```

| `MessagingBuilder` | |
|---|---|
| `Message<T>(name, typeInfo)` | Registers a type. The name defaults to the type's name; keep it stable, since messages in flight carry it |
| `Subscriber(name, …)` | A subscriber: 3–40 lowercase letters, digits and single hyphens. The name is also its queue's or subscription's |
| `Retention` | How long failed messages, cancel markers and shared payloads are kept, and a message's default time to live. 14 days |
| `CompressionThreshold` | Payloads above it are gzipped. 8 KB |
| `MaxDeliveries` | Deliveries without completing (the handler crashed or hung) before a message is kept as failed. 10 |
| `MaxPollInterval` | How long an idle pump waits at most between polls. 5 s |
| `JanitorInterval` | How often the janitor's hosted service runs. One hour |
| `JsonOptions` | Serialiser options for types without a `JsonTypeInfo`. Web defaults |

| `SubscriberBuilder` | |
|---|---|
| `Handles<T>()` | Takes messages of `T`. Several calls take several types |
| `Where(key, value)` | Takes only messages whose metadata has `key` equal to `value`. Several must all match |
| `Prioritized()` | A lane per priority |
| `MaxConcurrency(n)` | Messages one pump handles at a time. 4 |
| `Retry(attempts, first, max)` | Attempts in all, and the backoff. 5, from 10 s to 10 min |

A declaration that cannot work is refused when the host starts, with a message that says why. Examples:
- a subscriber of a type that is not registered
- two queues with the same name
- a subscriber name Azure would reject

---

## Publishing

```csharp
public sealed class ArenaController(IMessagePublisher publisher)
{
    public Task<PublishResult> OrderAsync(Competition race) =>
        publisher.PublishAsync(new ResizeImage(race.Id, picture, 800), new PublishOptions
        {
            Priority = MessagePriority.High,
            Metadata = { ["region"] = race.Region },
            DeduplicationId = $"arena|{race.Id}|v3",
        });
}
```

| `PublishOptions` | |
|---|---|
| `Priority` | `Low`, `Normal` (default) or `High` |
| `DeliverAt`, `Delay` | When it is due; it stays dormant until then |
| `Metadata` | Strings that travel with it, for filters and handlers. Keys may not start with `spine-` |
| `MessageId` | Unset, a new time-ordered id |
| `CorrelationId` | Ties messages of one flow together |
| `DeduplicationId`, `DeduplicationWindow` | A repeat within the window (one hour by default) is dropped |
| `TimeToLive` | How long it may wait from when it is due; then it is dropped. Unset, `Retention` |

`PublishResult` says the message id, when it is due, which subscribers it went to, and whether it was a duplicate. A message that no subscriber takes is not sent, and is logged as a warning.

`PublishBatchAsync` sends many with the same options in as few calls as the transport allows: Service Bus batches, and Storage sends 16 at a time.

## Handling

```csharp
public sealed class ResizeImageHandler(IImageStore images) : IMessageHandler<ResizeImage>
{
    public async Task HandleAsync(ResizeImage message, MessageContext context, CancellationToken ct)
    {
        if (await images.ExistsAsync(message.Name))
            return;                                          // delivered twice: nothing to do

        await images.SaveAsync(message.Name, Resize(message.Picture, message.Width), ct);
    }
}
```

A handler is resolved in a new scope for every message. `MessageContext` holds the message's:
- id, type and subscriber
- metadata and priority
- attempt
- publish time, due time and correlation id

**At least once:** a message is delivered at least once, and its order is not guaranteed. Make handlers safe to run twice.

**Failures:**
- **A handler throws:** the message is retried after a backoff. When the attempts run out, it is kept as failed.
- **`UnrecoverableMessageException`:** the message is kept as failed at once, for problems no retry can fix.
- **No handler registered in the host:** the message is kept as failed too, with that reason.

## Running the handlers

### Azure Functions

One trigger per lane, each a single line. The Functions host scales on the length of each queue, so a high-priority message never waits behind a normal backlog.

```csharp
public sealed class WorkFunctions(MessageDispatcher dispatcher)
{
    [Function("WorkHigh")]
    public Task WorkHigh([QueueTrigger("work-high", Connection = "queues")] QueueMessage message, CancellationToken ct)
        => dispatcher.DispatchAsync("work", message, ct);

    [Function("Work")]
    public Task Work([QueueTrigger("work", Connection = "queues")] QueueMessage message, CancellationToken ct)
        => dispatcher.DispatchAsync("work", message, ct);

    [Function("WorkLow")]
    public Task WorkLow([QueueTrigger("work-low", Connection = "queues")] QueueMessage message, CancellationToken ct)
        => dispatcher.DispatchAsync("work", message, ct);
}
```

**On Service Bus**, the trigger is `[ServiceBusTrigger("spine-messages", "work-high", Connection = "servicebus")] ServiceBusReceivedMessage message`, and the call is the same.

**When `DispatchAsync` returns**, the trigger completes the message. It throws only when messaging itself failed, such as a store that cannot be reached, and the platform then hands the message out again.

**`host.json`** sets the host's concurrency:
- `queues.batchSize` for Storage Queues
- `serviceBus.maxConcurrentCalls` for Service Bus
- `serviceBus.maxAutoLockRenewalDuration`, so a long handler keeps its lock

**Low lanes in Functions:** each lane is its own trigger, so a low lane still runs alongside the others. To hold bulk work back, put the low trigger in a Functions app of its own with a lower maximum scale.

### ASP.NET Core and worker services

```csharp
builder.Services.AddSpineMessagePump("receipts", "work");   // one pump per subscriber
builder.Services.AddSpineMessagingJanitor();                 // hourly cleanup; one instance at a time does it
```

**A pump:**
- receives in batches
- runs up to `MaxConcurrency` handlers at a time
- keeps their locks alive
- completes them

**Priority in a pump is strict:** a lower lane is read only when every higher one is empty. When idle, the pump backs off to `MaxPollInterval`.

## Priority

A prioritised subscriber has three lanes. Every other subscriber takes every priority on one queue.

| Priority | Lane | Storage queue | Service Bus subscription |
|---|---|---|---|
| `High` | `work-high` | `work-high` | `work-high` |
| `Normal` | `work` | `work` | `work` |
| `Low` | `work-low` | `work-low` | `work-low` |

## Scheduling

```csharp
var receipt = await publisher.PublishAsync(new SendReceipt(order), new PublishOptions { Delay = TimeSpan.FromMinutes(30) });

await publisher.CancelAsync(receipt);   // until it is due
```

**Service Bus** schedules natively, for any length of time.

**Storage Queues** keep a message dormant for at most 7 days. A later message surfaces early, and the dispatcher sends it on with the time that is left, so long schedules work too, at one extra send a week.

**Cancelling** writes a marker in blob storage. A scheduled message checks for it when it comes due and is dropped if it finds one. Only scheduled messages make the check.

## Large payloads

The framework handles large payloads; the handler always gets the payload, wherever it travelled.

1. **Compression:** a payload above `CompressionThreshold` (8 KB) is gzipped.
2. **Offloading:** if it is still larger than the transport carries inline, it goes to blob storage, and the message carries the blob's path.
   - **Storage Queues:** about 46 KB inline, because a message is 64 KB and base64 takes a quarter.
   - **Service Bus:** 192 KB inline, under the Standard tier's 256 KB. Raise `MaxBodyBytes` on Premium.

**Deleting the blob:**
- **One subscriber:** it is deleted as soon as the message is handled.
- **Several subscribers:** the janitor deletes it when it expires.

## Retries and failed messages

**A failed attempt** comes back after a backoff. The delay doubles from `first` up to `max`, with a fifth either way of jitter, so a burst of failures does not return as a burst. The retry goes to the failing subscriber alone, even on Service Bus, where the broker routes.

**A message is kept as failed** in blob storage, with its payload, the error and the exception type, when it:
- runs out of attempts
- is unrecoverable
- cannot be read
- was delivered `MaxDeliveries` times without completing

**What the platform gave up on** (the poison queue of a Functions queue trigger, Service Bus's dead-letter queue) is moved there by the janitor, so there is one place to look.

```csharp
await foreach (var failed in failedMessages.ListAsync("work"))
    logger.LogWarning("{Id} {Type}: {Error}", failed.MessageId, failed.Type, failed.Error);

await failedMessages.ReplayAsync(failed);   // from attempt 1, to that subscriber alone
await failedMessages.DeleteAsync(failed);
```

## Cleanup

`MessagingJanitor` runs hourly: as a timer trigger in Functions, or with `AddSpineMessagingJanitor()`. A blob lease lets one instance at a time run it.

**What it removes:**
- offloaded payloads past their expiry
- failed messages and cancel markers older than `Retention`
- deduplication ids whose window has passed

**Dated paths:** everything lives under a dated path (`payloads/2026/10/13/…`), so it removes whole days rather than reading every item.

**Queue expiry:** Storage Queue messages also get a time to live, so Azure expires them itself.

## Aspire

**Storage**, on Azurite:

```csharp
var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var queues = storage.AddQueues("queues");
var blobs = storage.AddBlobs("blobs");

builder.AddProject<Projects.Api>("api").WithReference(queues).WithReference(blobs).WaitFor(blobs);
builder.AddAzureFunctionsProject<Projects.Functions>("functions").WithHostStorage(storage).WithReference(queues).WithReference(blobs);
```

**Service Bus**, on its emulator. It needs Docker:

```csharp
var servicebus = builder.AddAzureServiceBus("servicebus").RunAsEmulator();
var management = servicebus.GetEndpoint("emulatorhealth");

api.WithReference(servicebus).WithEnvironment("Messaging__ServiceBusManagement", management);
```

```csharp
m.UseServiceBus(configure: o => o.EmulatorManagementEndpoint = configuration["Messaging:ServiceBusManagement"]);
```

**The emulator's management port:** the emulator takes administration calls on port 5300, not on its AMQP port. Aspire maps that port, so pass its `emulatorhealth` endpoint to every host that provisions. Without Aspire, the default is port 5300 on the connection's host.

**On Apple silicon**, the emulator's SQL Server container crashes under emulation. The sample's AppHost swaps it for Azure SQL Edge, which runs natively.

**Connections in configuration:** both packages read connections by name, which is what `WithReference` sets:
- `ConnectionStrings:queues`, `ConnectionStrings:blobs` and `ConnectionStrings:servicebus`
- or a setting of that name

**Managed identity:** a service URI (Storage) or a namespace (Service Bus) needs a `Credential` in the options, such as `DefaultAzureCredential`.

**Telemetry:** to see a message from publish to handler in one trace, add messaging's `ActivitySource` and `Meter` to OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(MessagingTelemetry.Name))
    .WithMetrics(m => m.AddMeter(MessagingTelemetry.Name));
```

**The metrics:**
- `spine.messaging.published`, `.handled`, `.retried`, `.failed` and `.offloaded`
- `spine.messaging.handle.duration`
- `spine.messaging.delivery.latency`

## Service Bus provisioning

At startup the host creates:
- the topic (`spine-messages`)
- a subscription per lane
- each subscription's rules

**The rules** are a correlation filter per message type the subscriber takes, on the subject and the metadata in `Where`, plus one for messages addressed to the subscriber alone (retries and replays). Rules it did not make, such as `$Default`, are removed. A changed filter is replaced at the next start.

**Without the Manage right:** set `Provision = false` where the identity lacks it, and create the entities with infrastructure as code from the same names. A provisioning failure says so in the log, with what to change.

## Choosing a transport

| | Storage Queues | Service Bus |
|---|---|---|
| Cost | Per operation, no base charge | A monthly base plus operations; topics need the Standard tier or higher |
| Inline payload | About 46 KB | 192 KB (Standard), more on Premium |
| Scheduling | Up to 7 days per hop, sent on automatically | Native, any length |
| Routing | On the publishing side: one send per matching subscriber | In the broker: one send, correlation filters |
| Idle pump | Polls, backing off to 5 s | Polls with a short wait |
| Dead letters | Functions' `-poison` queue | Built-in dead-letter queue |
| Local | Azurite | The emulator (Docker) |

## Throughput

`ThroughputTests` publishes 10,000 small messages in batches and handles them with one pump of 32 handlers. It runs on one Mac, against the local emulators, with `SPINE_MESSAGING_THROUGHPUT=1`:

| | Publish | Handle |
|---|---|---|
| In memory (the framework alone) | 350,000/s | 16,000/s |
| Azurite, 1,000 messages | 600/s | 700/s |
| Azurite, 10,000 messages | 1,000/s | 75/s |
| Service Bus emulator, 10,000 messages | 95–130/s | 70/s |

**What the numbers show:**
- The framework's own cost is small.
- The emulators set the pace. Azurite slows as a queue grows, and the Service Bus emulator keeps its state in SQL.
- The cloud services are much faster, and Functions scale out across instances.
- Measure there before sizing.

## Testing

`UseInMemory()` runs the same declaration in the process, so tests need no storage. Give it a `FakeTimeProvider` to move scheduled messages and retries along. `InMemoryTransport.Peek(lane)` shows what is queued.

The repository's integration tests run against Azurite and the Service Bus emulator when they answer on their usual ports:

```bash
docker run -d -p 10000-10002:10000-10002 mcr.microsoft.com/azure-storage/azurite
```

The Service Bus emulator needs its SQL container and a config file; Microsoft's [emulator installer](https://github.com/Azure/azure-service-bus-emulator-installer) sets both up. On Apple silicon, use `mcr.microsoft.com/azure-sql-edge` as the SQL image.

## Sample

`samples/SpineMessagingSample.*` is an Aspire AppHost with three projects:
- **An API** that publishes every kind of message and runs pumps for `receipts` and `flaky`.
- **A Functions app** that handles `work` with a trigger per lane, and runs the janitor on a timer.
- **A shared library** with the declaration and the handlers.

```bash
dotnet run --project samples/SpineMessagingSample.AppHost
```

```bash
dotnet run --project samples/SpineMessagingSample.AppHost -- --Messaging:Transport=servicebus
```

`send.http` in the API project walks through the features:
- pictures large enough for blob storage, at two priorities
- a low-priority job
- a scheduled receipt and its cancellation
- a duplicate
- a job that fails twice, and one that ends among the failed messages

Watch the Aspire dashboard's traces and logs.
