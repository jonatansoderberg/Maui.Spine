# Issue #540 — Spine.Server.Messaging: queues with routing, priority, scheduling, large payloads and cleanup (Storage Queues, Service Bus)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/540
**Branch:** issue/540-spineservermessaging-queues-with-routing-priority
**Status:** In Progress

This issue is step 2.3a of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), tracked in `docs/plans/ai-and-security.md`. The owner moved it out of the generated-asset pipeline (#511) on 2026-10-13 and asked for it first. #511 then builds on it.

## Plan

### 0. Goal

**A general queue layer for Spine's server packages.** The app puts a typed message on a queue, and one or more Azure Functions (with triggers) or hosted workers handle it. The framework takes care of the rest:
- large payloads
- retries and failed messages
- priority
- routing
- scheduling
- cleanup

**One API, several transports, each in its own library:**
- Azure Storage Queues: simple and cheap.
- Azure Service Bus: more features, more cost.
- In memory: for tests.

Other transports can be added later. Everything runs under Aspire: Azurite for Storage, and the Service Bus emulator in Docker.

**Delivery is at least once.** Order is not guaranteed, and handlers must be idempotent. The docs say so.

### 1. Packages

| Package | Contents | Depends on |
|---|---|---|
| `Plugin.Maui.Spine.Server.Messaging` | The abstractions and the topology builder, plus `MessagePublisher`, `MessageDispatcher`, `MessagePump` and `MessagingJanitor`. Also the retry policy, claim check, compression, telemetry, and an in-memory transport and stores | `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Options`, `System.Text.Json` |
| `Plugin.Maui.Spine.Server.Messaging.AzureStorage` | `StorageQueueTransport`, and `BlobMessageStore`: payloads, failed messages and markers in one blob container, plus the janitor lease. Also the Functions entry point for a `QueueMessage` | `.Server.Messaging`, `Azure.Storage.Queues`, `Azure.Storage.Blobs` |
| `Plugin.Maui.Spine.Server.Messaging.ServiceBus` | `ServiceBusTransport`, which provisions its topic, subscriptions and rules at startup. Also the Functions entry point for a `ServiceBusReceivedMessage` | `.Server.Messaging`, `.AzureStorage` (the blob store), `Azure.Messaging.ServiceBus` |

### 2. Setup

The topology is declared once, in code the publisher and the consumers share (a class library or a shared method). The Storage transport fans out on the publishing side, so the publisher must know the subscribers.

```csharp
builder.AddAzureQueueServiceClient("queues");   // Aspire's client integrations, or ConnectionStrings:{name}
builder.AddAzureBlobServiceClient("blobs");

builder.Services.AddSpineMessaging(m =>
{
    m.UseAzureStorage(queues: "queues", blobs: "blobs");   // or m.UseServiceBus("servicebus", blobs: "blobs")

    m.Message<ArenaImageOrder>("arena-image");               // stable wire name; default typeof(T).Name
    m.Message<WelcomeMail>("welcome-mail", AppJson.Default.WelcomeMail);  // optional JsonTypeInfo (AOT, speed)

    m.Subscriber("images", s => s
        .Handles<ArenaImageOrder>()
        .Where("region", "north")              // metadata equals; several Where = AND, several Handles = OR
        .Prioritized()                         // high, normal and low lanes
        .MaxConcurrency(2)
        .Retry(attempts: 5, first: TimeSpan.FromSeconds(10), max: TimeSpan.FromMinutes(10)));

    m.Subscriber("mail", s => s.Handles<WelcomeMail>());

    m.Retention = TimeSpan.FromDays(14);       // payloads, failed messages and markers
});

builder.Services.AddMessageHandler<ArenaImageOrder, ArenaImageHandler>();
builder.Services.AddSpineMessagePump("images");  // only in a host that consumes in-process
```

**Publishing:**

```csharp
await publisher.PublishAsync(new ArenaImageOrder(eventId, season), new PublishOptions
{
    Priority = MessagePriority.High,
    DeliverAt = race.Start.AddHours(-2),         // or Delay
    Metadata = { ["region"] = "north" },
    DeduplicationId = $"arena|{eventId}|v3",     // optional; a repeat within the window is dropped
});
```

**Azure Functions:** one trigger per lane, each a single line that hands over to the dispatcher. The host scales on the length of each queue.

```csharp
[Function("images")]
public Task Images([QueueTrigger("images", Connection = "queues")] QueueMessage message, CancellationToken ct)
    => dispatcher.DispatchAsync("images", message, ct);

[Function("images-high")]
public Task ImagesHigh([QueueTrigger("images-high", Connection = "queues")] QueueMessage message, CancellationToken ct)
    => dispatcher.DispatchAsync("images", message, ct);

[Function("messaging-janitor")]
public Task Janitor([TimerTrigger("0 15 * * * *")] TimerInfo timer, CancellationToken ct) => janitor.RunAsync(ct);
```

### 3. Key types

| Type | Role |
|---|---|
| `IMessagePublisher` | `PublishAsync<T>(T, PublishOptions?, ct)` returns `PublishResult` (`MessageId`, `IsDuplicate`). Also `PublishBatchAsync<T>(…)` and `CancelAsync(messageId)` for a scheduled message |
| `PublishOptions` | `Priority`, `DeliverAt` / `Delay`, `Metadata`, `MessageId`, `CorrelationId`, `DeduplicationId` (+ `DeduplicationWindow`), `TimeToLive` |
| `MessagePriority` | `Low`, `Normal`, `High` |
| `IMessageHandler<T>` | `HandleAsync(T message, MessageContext context, CancellationToken ct)` |
| `MessageContext` | `MessageId`, `Type`, `Metadata`, `Priority`, `Attempt`, `EnqueuedAt`, `DeliverAt`, `CorrelationId`, `Subscriber` |
| `UnrecoverableMessageException` | Thrown by a handler: no retry, straight to the failed messages |
| `MessagingBuilder`, `SubscriberBuilder` | The setup above. The result is a `MessageTopology`: message types, subscribers, filters, lanes and policies |
| `IMessageTransport` | What a transport implements: `ProvisionAsync(topology)`, `SendAsync(IReadOnlyList<OutgoingMessage>)`, `ReceiveAsync(lane)` yielding `ITransportDelivery`, and `PurgeAsync(olderThan)` |
| `ITransportDelivery` | `Envelope`, `Attempt`, `CompleteAsync`, `RetryAsync(delay)`, `ExtendAsync` (visibility or lock), `DeadLetterAsync` |
| `MessageEnvelope` | The wire form: id, type, metadata, priority, attempt, enqueued, due, correlation, trace parent, content encoding, and the body inline or as a payload reference |
| `IMessageStore` | Blob-shaped state: offloaded payloads, failed messages, deduplication and cancel markers, the janitor's lease. Implemented by `BlobMessageStore` and `InMemoryMessageStore` |
| `IFailedMessages` | `ListAsync(subscriber)`, `ReplayAsync(id)`, `DeleteAsync(id)` |
| `MessageDispatcher` | Shared by the pump and Functions: reads the envelope, fetches an offloaded body, checks due time and cancel markers, calls the handler, and applies the retry policy |
| `MessagePump` | A `BackgroundService` per subscriber and lane for ASP.NET Core and worker hosts |
| `MessagingJanitor` | Cleanup on a schedule, as a timer trigger or a hosted service |

### 4. How each feature works

**Large payloads (claim check):**
- A body above 8 KB is gzipped.
- A body still above the transport's limit goes to the blob container as `payloads/{expiry date}/{id}`, and the envelope carries the reference. The limit is 48 KB raw for Storage (64 KB base64 with room for the envelope) and 192 KB for Service Bus Standard (configurable for Premium).
- The dispatcher fetches the payload before the handler runs, so the handler never knows the difference.
- With one recipient, the blob is deleted when the message completes. With fan-out, the janitor deletes it after the retention period.

**Routing:**
- A subscriber takes message types (OR) and metadata equals (AND), which is what a Service Bus correlation filter can express.
- **Storage:** every subscriber is a queue (`{name}`, and `{name}-high` when it is prioritised). The publisher evaluates the filters and sends one copy per matching queue.
- **Service Bus:** one topic for the app, with one subscription per subscriber and lane. Each has `CorrelationRuleFilter`s on `Subject` (the type) and on application properties (metadata, priority). Service Bus does the fan-out.

**Priority:**
- **Lanes:** a prioritised subscriber has three lanes, `{name}-high`, `{name}` and `{name}-low`. A subscriber that is not prioritised takes every priority on one queue.
- **Pump:** it is strict. It empties high before normal, and normal before low.
- **Functions:** each lane is its own trigger, so high never waits behind a normal or low backlog.
  - Low still runs alongside the others.
  - To hold bulk work back, the low trigger can live in a Functions app of its own with a lower maximum scale. The docs show how.

**Scheduling:**
- **Service Bus:** `ScheduledEnqueueTime`, natively and with no limit.
- **Storage:** the message is sent with a visibility delay of up to 7 days. A message that surfaces before it is due is sent again with the remaining delay (at most 7 days per hop) and the old one is completed.
- **`CancelAsync(id)`** writes a cancel marker. Only messages published with a due time check for it, so other messages pay nothing.

**Retries and failed messages:**
- **When a handler throws:**
  - Below `MaxAttempts`, the message comes back after an exponential backoff with jitter (by default 10 s to 10 min, 5 attempts). On Storage the pump only changes the visibility; elsewhere a copy with `Attempt + 1` is scheduled.
  - At the last attempt, or on `UnrecoverableMessageException`, the envelope and the error are saved as a failed message (`failed/{subscriber}/{date}/{id}.json`), and the delivery completes.
- **The native poison queue and dead-letter queue** catch only crashes, where a process died mid-message. The janitor moves what lands there into the failed messages, so there is one place to list and replay from.

**Deduplication:**
- Optional, per message. `DeduplicationId` creates a marker blob `dedup/{date}/{hash}` with If-None-Match: *.
- A repeat inside the window returns `IsDuplicate` and sends nothing. #511 can use this to order an image once.

**Cleanup (`MessagingJanitor`):**
- **The blob layout is dated,** so cleanup removes whole date prefixes older than the retention period: payloads, failed messages, and dedup and cancel markers. That costs one list per day, not one call per message.
- **Old native poison and dead-letter messages** are moved and then deleted.
- **Storage queue messages get a time to live** (their due time plus the retention), so Azure expires them itself.
- **One instance at a time:** a blob lease makes sure only one janitor runs. It runs hourly, as a hosted service next to a pump or as a timer trigger in Functions.

**Performance and scale:**
- **Clients are singletons,** one per queue. Entities are provisioned once at startup, not per call.
- **The Storage pump:**
  - receives in batches of 32
  - runs handlers in parallel up to `MaxConcurrency`
  - extends visibility while a handler runs
  - polls adaptively, from 50 ms up to 5 s when idle. The idle cost is about one transaction every 5 s per queue, which is cents a month.
- **The Service Bus side** uses `ServiceBusProcessor` with prefetch and automatic lock renewal.
- **Batch publishing** uses Service Bus batches, and parallel sends for Storage.
- **`System.Text.Json`** takes an optional `JsonTypeInfo` per type.
- **A throughput test** against Azurite and the emulator sends 10,000 messages, and its numbers go into the docs.

**Telemetry:**
- An `ActivitySource` and a `Meter`, both named `Plugin.Maui.Spine.Server.Messaging`.
- **Spans:** publish and handle, linked through a W3C `traceparent` in the envelope, so a message shows end to end in the Aspire dashboard.
- **Metrics:** published, handled, retried, failed and offloaded counts, plus handling time and delivery latency.

**Aspire:**
- **Storage:** `AddAzureStorage("storage").RunAsEmulator()` with `.AddQueues("queues")` and `.AddBlobs("blobs")`.
- **Service Bus:** `AddAzureServiceBus("servicebus").RunAsEmulator()`.
- **Functions** get the same references, and the triggers' `Connection` names the resource.
- **Clients:** the transports take the clients from DI when Aspire's client integrations registered them, so managed identity works. Otherwise they build them from `ConnectionStrings:{name}`.

### 5. Verification

- **Unit tests** in `tests/Plugin.Maui.Spine.Server.Messaging.Tests`, on the in-memory transport with `FakeTimeProvider`:
  - routing and fan-out, filters, priority order
  - scheduling, the Storage hop past 7 days, and cancel
  - retries and backoff, `UnrecoverableMessageException`, failed messages and replay
  - claim check and compression, deduplication
  - the janitor's prefixes and lease
- **Integration tests** against Azurite and the Service Bus emulator in Docker. They run when `SPINE_MESSAGING_INTEGRATION=1`, and are skipped otherwise.
- **A sample under Aspire** in `samples/` (an AppHost, an API that publishes, a Functions app that consumes, and a pump in the API), run on both transports: Azurite, and the Service Bus emulator in Docker. An API publishes, and Functions and a pump consume. Scheduled, high-priority, large and failing messages are checked in the Aspire dashboard's traces, and the janitor is run.

### 6. Documentation and the plan

- **Docs:**
  - the package READMEs
  - `docs/wiki/messaging.md`: setup, Functions, Aspire, transports compared on cost, limits and features, and cleanup
  - `packages.md` and the root README
  - the `spine-setup` skill
- **The roadmap:**
  - step 2.3a is this issue, and 2.3b (#511) depends on it
  - Decisions and the Verification backlog
  - a note in #511 that it builds on #540

### 7. All types

Public unless marked *internal*.

**`Plugin.Maui.Spine.Server.Messaging`:**
- **Publishing:**
  - `IMessagePublisher`, and *internal* `MessagePublisher`
  - `PublishOptions`, `PublishResult`, `MessagePriority`
- **Handling:**
  - `IMessageHandler<T>`, `MessageContext`, `UnrecoverableMessageException`
  - `MessageDispatcher`
- **Setup:**
  - `ServiceCollectionExtensions`, with `AddSpineMessaging`, `AddMessageHandler<TMessage, THandler>` and `AddSpineMessagePump`
  - `MessagingBuilder`, `SubscriberBuilder`
  - `RetryPolicy`
- **Topology:** `MessageTopology`, `MessageType`, `Subscriber`, `SubscriberFilter`, `MessageLane`
- **Failed messages and cleanup:**
  - `IFailedMessages`, and *internal* `FailedMessages`
  - `FailedMessage`
  - `MessagingJanitor`
- **The transport contract** (namespace `.Transport`):
  - `IMessageTransport`, `ITransportDelivery`, `TransportLimits`
  - `MessageEnvelope`, `OutgoingMessage`, `PayloadReference`
  - `IMessageStore`
- **In memory:** `InMemoryTransport`, `InMemoryMessageStore`, `InMemoryMessagingExtensions` (`UseInMemory`)
- *internal:*
  - `MessageRouter` (filters to lanes)
  - `EnvelopeCodec` (JSON, gzip, offload)
  - `MessagePump`
  - `MessagingTelemetry` (`ActivitySource`, `Meter`)

**`Plugin.Maui.Spine.Server.Messaging.AzureStorage`:**
- `AzureStorageMessagingExtensions` (`UseAzureStorage`)
- `AzureStorageMessagingOptions`
- `StorageQueueTransport`
- `BlobMessageStore`
- `StorageQueueDispatchExtensions` (`DispatchAsync(subscriber, QueueMessage, ct)`)
- *internal:*
  - `StorageQueueDelivery`
  - `StorageQueueNames` (name rules and validation)

**`Plugin.Maui.Spine.Server.Messaging.ServiceBus`:**
- `ServiceBusMessagingExtensions` (`UseServiceBus`)
- `ServiceBusMessagingOptions`
- `ServiceBusTransport`
- `ServiceBusDispatchExtensions` (`DispatchAsync(subscriber, ServiceBusReceivedMessage, ct)`)
- *internal:*
  - `ServiceBusDelivery`
  - `ServiceBusProvisioner` (topic, subscriptions, rules)
  - `ServiceBusEnvelopeMapper` (envelope to and from `ServiceBusMessage`)

**Tests, in `tests/Plugin.Maui.Spine.Server.Messaging.Tests`:**
- `RoutingTests`, `PriorityTests`, `SchedulingTests`, `RetryTests`
- `ClaimCheckTests`, `DeduplicationTests`, `FailedMessagesTests`, `JanitorTests`
- `StorageQueueIntegrationTests`, `ServiceBusIntegrationTests`

**The sample, in `samples/SpineMessagingSample.*`:**
- **The projects:**
  - `.AppHost`
  - `.Shared`, holding the topology, the messages and the handlers
  - `.Api`, which publishes and runs a pump
  - `.Functions`
- **The messages:**
  - `ResizeImage`: a large payload
  - `SendReceipt`: scheduled
  - `RebuildIndex`: low priority
  - `FlakyJob`: fails, then retries, then ends up among the failed messages
- **The handlers and functions:**
  - `ResizeImageHandler`, `SendReceiptHandler`, `RebuildIndexHandler`, `FlakyJobHandler`
  - `QueueFunctions`, `JanitorFunction`

## Open Questions

None. The owner answered all four on 2026-10-13; see Decisions.

## Changes

- **`src/Plugin.Maui.Spine.Server.Messaging`:**
  - `AddSpineMessaging` with `MessagingBuilder` and `SubscriberBuilder`, and `MessageTopology` (types, subscribers, lanes, routing).
  - `IMessagePublisher`: publish, batch, cancel.
  - `IMessageHandler<T>` and `MessageContext`.
  - `MessageDispatcher`:
    - expiry, crash backstop, early hop, cancel check, payload read-back
    - retries as scheduled copies to the subscriber alone, and failed messages
  - `MessagePump`, which receives in batches when half its capacity is free, honours strict priority and renews locks.
  - `MessagingJanitor`: purge, rescue of dead letters, one instance at a time; plus `AddSpineMessagingJanitor`.
  - `IFailedMessages`: list, replay, delete.
  - `EnvelopeCodec`: gzip above 8 KB, offload above the transport's limit.
  - `MessagingTelemetry`: `ActivitySource` and `Meter`, with the trace carried in the envelope.
  - The transport contract (`IMessageTransport`, `ITransportDelivery`, `MessageEnvelope`, `IMessageStore`), and `InMemoryTransport` and `InMemoryMessageStore` with `UseInMemory`.
- **`src/Plugin.Maui.Spine.Server.Messaging.AzureStorage`:**
  - `StorageQueueTransport`: a queue per lane, fan-out on publish, delays of 7 days with hops beyond.
  - `StorageQueueWire`: compact JSON, with the payload inline as raw JSON.
  - `BlobMessageStore`: dated paths, conditional-create deduplication, lease-based lock, purge by day.
  - `UseAzureStorage` and `UseBlobStore`.
  - `DispatchAsync(QueueMessage)` for Functions.
- **`src/Plugin.Maui.Spine.Server.Messaging.ServiceBus`:**
  - `ServiceBusTransport`: one topic, a subscription per lane, batches, native scheduling.
  - `ServiceBusProvisioner`: creates the entities and reconciles the correlation rules; `$Default` is removed.
  - `ServiceBusEnvelopeMapper`.
  - `UseServiceBus`, with `EmulatorManagementEndpoint` for the emulator's port 5300.
  - `DispatchAsync(ServiceBusReceivedMessage)` for Functions.
- **Tests:** `tests/Plugin.Maui.Spine.Server.Messaging.Tests`, 50 tests.
  - **Unit tests on the in-memory transport and a fake clock (38):** routing, priority (with the real pump), scheduling and hops, retries, claim check, deduplication, failed messages, janitor.
  - **Integration tests:**
    - against Azurite (6)
    - against the Service Bus emulator (6)
  - **`ThroughputTests`** (opt-in).
- **Sample:** `samples/SpineMessagingSample.*`, an Aspire AppHost that runs on either transport.
  - `.Shared`: the declaration and the handlers.
  - `.Api`: publishes, runs pumps for `receipts` and `flaky`, and has `send.http`.
  - `.Functions`: a trigger per lane for each transport (the AppHost disables the other set) and the janitor timer.
- **Repository:**
  - the icon `server-messaging.png`
  - `Directory.Packages.props`, `Spine.slnx` and `Spine.Packages.slnf`
- **Docs:**
  - the three package READMEs and `docs/wiki/messaging.md`
  - `packages.md` and the root README (twenty-seven packages)
  - the `spine-setup` skill
  - the roadmap: step 2.3a, the Decisions and the Verification backlog
- **Verified:**
  - **The tests:** all 50 pass, with Azurite and the emulator in Docker.
  - **The sample under Aspire on Storage:**
    - 100 KB pictures went through blob storage, and the blobs were deleted after.
    - Functions handled all three work lanes.
    - The receipt was sent when due, and the duplicate was dropped.
    - The flaky job ended failed after 3 attempts.
    - One trace runs from POST /images through ten publishes to ten handlers in Functions.
  - **The sample on the Service Bus emulator:** the same, plus a cancelled receipt that was dropped when due, and a janitor run.
  - **Throughput (10,000 messages):**
    - in memory: 16,000 handled/s
    - Azurite: 75/s at 10,000 queued and 700/s at 1,000, so Azurite slows with queue length
    - the emulator: 70/s

## Decisions

- **2026-10-13, owner:** Messaging is its own issue, general and not specific to assets, and comes before #511. Each transport is its own library; Storage Queues is the cheap one and Service Bus the richer one. It must:
  - offload large payloads to blob storage
  - clean up old data
  - support optional priority, message types with metadata, routing to subscribers, and scheduling
  - scale, and be easy to set up and run under Aspire
- **2026-10-13, owner:** Everything is in #540, one PR with both transports.
- **2026-10-13, owner:** Three priority levels: `Low`, `Normal` and `High`.
- **2026-10-13, owner:** Blob storage is required for every transport, Service Bus included.
- **2026-10-13, owner:** A runnable Aspire sample lives in `samples/`.
- **2026-10-13, Claude:** Service Bus provisions its topic, subscriptions and rules at startup with `ServiceBusAdministrationClient` when `Provision` is on (the default in Development). It is off in production when the identity lacks Manage rights, and infrastructure as code creates the entities instead. Whether the emulator accepts administration calls is checked first. If it does not, the sample's AppHost declares the entities with Aspire's `AddServiceBusTopic` and `AddServiceBusSubscription`.
- **2026-10-13, Claude:** The emulator takes administration calls on port 5300, not on its AMQP port. The spike created a topic, a subscription, a correlation rule and a queue there. **So** provisioning from code works on the emulator too. Under Aspire, which maps the port, the AppHost passes the `emulatorhealth` endpoint as `EmulatorManagementEndpoint` to every host that provisions.
- **2026-10-13, Claude:** A retry is a scheduled copy with `Attempt + 1`, sent to the failing subscriber alone. A replay and a Storage hop past 7 days are sent the same way. **Why:**
  - The attempt count travels with the message, the same on every transport and in Functions, where the host settles the delivery.
  - A backoff is possible on Service Bus, whose abandon has no delay.
  - On Service Bus, a copy carries `spine-to = <subscriber>` and matches only that subscriber's `direct` rule, while published messages carry `spine-to = *`, which every type rule requires.
- **2026-10-13, Claude:** A scheduled message is cancelled by id and due time (`CancelAsync(PublishResult)`). **Why:** the marker lives under the due date's folder, so the janitor can remove it by day, and only scheduled messages check for it.
- **2026-10-13, Claude:** Deduplication is a conditional blob create with the expiry in metadata. An expired id is taken over with an ETag condition. **Why:** it works the same on every transport. Service Bus's own duplicate detection needs the Standard tier, an entity setting and a fixed window.
- **2026-10-13, Claude:** On Storage Queues, an uncompressed payload travels as raw JSON inside the envelope's JSON. Queue messages are base64 by default, which Functions' queue trigger expects. **Why:** a payload is not base64-encoded twice, and the inline limit is about 46 KB.
- **2026-10-13, Claude:** Every host provisions at startup, and a failure is logged and tried again on the next publish or receive. **Why:** Azurite or the emulator may still be starting under Aspire. A Service Bus failure names the fix: the Manage right, `Provision = false`, or the emulator's management endpoint.
- **2026-10-13, Claude:** Queues and subscriptions of a subscriber that is removed from the declaration are left in place. **Why:** they may hold messages, and deleting entities is the owner's call.
- **2026-10-13, Claude:** The sample's AppHost runs the emulator's SQL container as Azure SQL Edge on Apple silicon. **Why:** Aspire's `mssql/server:2022` image crashed under qemu (segfault, exit 139), and the emulator stopped on its SQL health check.
- **2026-10-13, Claude:** The pump receives again once half its handlers are free. **Why:** a busy pump otherwise fetched one message per finished handler. The throughput test showed the limit was the emulators, not the pump.
- **2026-10-13, Claude:** Departures from the approved type list:
  - **`MessageRouter` became `MessageTopology.Route`.**
  - **Added public types:**
    - `JanitorReport`
    - `MessagingTelemetry` (its `Name`)
    - `UseBlobStore` (Service Bus and custom transports use it)
    - `AddSpineMessagingJanitor`
    - `ServiceBusMessagingOptions.EmulatorManagementEndpoint` and `.Container`
  - **Added internal types:** `MessagingRuntime` (shared state and one-time provisioning), `StorageQueueWire` and `AzureStorageConnections`.
- **2026-10-13, Claude:** Failed messages live in the blob store for every transport, and the native poison and dead-letter queues are only a backstop for crashes. **Why:** one place to list, replay and clean up, the same on Storage and Service Bus.
