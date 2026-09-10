# Push (server)

`Plugin.Maui.Spine.Server` is the backend half of Spine.Push: the register of devices, the tag
expressions that address them, the transports that reach APNs and FCM, and the endpoints the app
registers through. It is a plain `net10.0` library — no MAUI — so an ASP.NET Core service or an
Azure Functions isolated worker can reference it directly.

The app half is [`Plugin.Maui.Spine.Push`](push.md). The two share `Plugin.Maui.Spine.Common`, which is where
`PushInstallation`, the `spine.*` keys and the widget tree model live.

---

## Why the register is yours

Spine sends straight to Apple and Google rather than through a hosted service. What such a service
solves — storing tokens, fanning out, matching tags — is a table and a loop at this scale, and what
it does not solve is exactly what Spine is for: Live Activities, widgets, and Windows. Everything
sits behind `IPushTransport`, so a hub-backed transport can be added later without the app noticing.

---

## Setup

```csharp
services.AddSpinePush(o =>
{
    o.Apple(a =>
    {
        a.TeamId     = cfg["Push:Apple:TeamId"];
        a.KeyId      = cfg["Push:Apple:KeyId"];
        a.PrivateKey = cfg["Push:Apple:PrivateKey"];   // the contents of the .p8 file
        a.BundleId   = "se.cosmomedia.orientera";
    });

    o.Android(f => f.ServiceAccountJson = cfg["Push:Fcm:ServiceAccount"]);

    o.UseInMemoryStore();

    o.AllowTags = (installation, tags) =>
        tags.Where(t => !t.StartsWith("user:") || t == $"user:{installation.UserId}");

    o.Authenticate = request => ValueTask.FromResult(request.Headers.Authorization == expected);
});
```

`AddSpinePush` validates as it goes: a missing team id or a server with no register throws with the
name of what is missing, rather than failing on the first send.

| Setting | What it does |
|---|---|
| `Apple(...)` | APNs credentials. `Environment` defaults to `PerInstallation`, which trusts what each device reported |
| `Android(...)` | The Firebase service account JSON |
| `UseInMemoryStore()` | Keeps the register in the process. For tests and sample servers |
| `UseStore(...)` | A register of your own — see [The register](#the-register) |
| `AllowTags` | Narrows the tags a client may register. Runs on every registration |
| `Authenticate` | Gates the registration endpoints. Unset accepts everything |

---

## Sending

```csharp
public interface IPushSender
{
    Task<PushResult> SendAsync(PushTarget target, PushNotification notification, CancellationToken ct = default);
    Task<PushResult> SendSilentAsync(PushTarget target, IReadOnlyDictionary<string, string> data, CancellationToken ct = default);
    Task<PushResult> StartLiveActivityAsync(PushTarget target, string kind, LiveActivityLayout layout, PushAlert alert, LiveActivityOptions? options = null, CancellationToken ct = default);
    Task<PushResult> UpdateLiveActivityAsync(PushTarget target, string kind, LiveActivityLayout layout, LiveActivityEvent @event = LiveActivityEvent.Update, LiveActivityOptions? options = null, CancellationToken ct = default);
    Task<PushResult> RefreshWidgetsAsync(PushTarget target, string? kind = null, CancellationToken ct = default);
}
```

One call reaches every platform the target matched. Spine builds an `aps` alert for APNs and a
data-only message for FCM, so the app's handler sees the same thing on both.

```csharp
await push.SendAsync(
    PushTarget.Tags("kind:results-published && competition:59691"),
    new PushNotification
    {
        Title = "Resultat klara",
        Body  = "Gävle OK Medeldistans",
        Route = "competition/59691",
        Channel = "results",
    });
```

### Targets

| Target | Reaches |
|---|---|
| `PushTarget.Tags("a && !b")` | Every installation whose tags satisfy the expression |
| `PushTarget.Installation(id)` | One registration |
| `PushTarget.User("121330")` | Everything tagged `user:121330` |
| `PushTarget.All` | Every registration |

### Tag expressions

Tags combine with `&&`, `||` and `!`, grouped with parentheses, in the syntax Azure Notification
Hubs uses — with no limit on how many tags one expression may name. Tags are opaque strings compared
with ordinal equality, so `user:ABC` and `user:abc` are different tags.

```csharp
PushTagExpression.Parse("kind:pm-published && (competition:1 || competition:2) && !muted");
```

`TryParse` returns the reason and the position instead of throwing.

### Notifications

| Property | Maps to |
|---|---|
| `Route` | `spine.route`; the page the app opens when tapped |
| `Channel` | The Android channel, and the thread id iOS groups by |
| `CollapseId` | `apns-collapse-id` and `collapse_key` |
| `TimeToLive` | `apns-expiration` and FCM's `ttl` |
| `Priority` | APNs 10 or 5, FCM high or normal |
| `Interruption` | `interruption-level`: passive, active, time-sensitive |
| `Apple` / `Android` | Hooks that adjust the built payload before it is sent |

### Live Activities

```csharp
await push.UpdateLiveActivityAsync(
    PushTarget.User("121330"),
    kind: "din-start:59691",
    MyStartActivity.Layout(competition, start, now),   // the same C# the app builds with
    LiveActivityEvent.Update,
    new LiveActivityOptions { StaleAt = now.AddMinutes(10) });
```

The layout is serialized into the payload's `content-state`, and the topic gets Apple's
`.push-type.liveactivity` suffix. Priority defaults to 5: priority 10 counts against the hourly
budget, so ask for it only when the update cannot wait.

Android gets the same call as a high-priority data message carrying `spine.layout`, which the app's
service hands to `ILiveActivityService`.

**The 4 KB ceiling is real.** APNs refuses a larger payload, so `PushPayloads` measures what it
built and throws with the size rather than letting it become a 413. For scale: Orientera's Live
Activity, with a competition name, a place and a timer, serializes to about 1.2 KB.

### Broadcast channels

When many devices show the same thing — everyone following competition X — one push can reach them
all. The app starts its activity on a channel, and the server sends to the channel instead of to
each activity's token:

```csharp
// Once per competition, on the server:
var channel = await channels.CreateAsync(PushChannelStorage.MostRecent);   // IPushChannels

// In the app, which got the id from your backend:
await liveActivities.StartAsync("tavling:4711", layout, channel: channel);

// Every update after that, one call for everyone:
await push.BroadcastLiveActivityAsync(channel, "tavling:4711", layout);
```

- **iOS 18 follows the APNs channel**, and Apple fans the push out. It needs the **Broadcast
  capability** on the App ID (Certificates, Identifiers & Profiles → the App ID → Push
  Notifications); without it APNs refuses to create a channel.
- **Android follows an FCM topic** named after the channel (`LiveActivityChannels.Topic`).
  Spine.Push subscribes while an activity on the channel runs and unsubscribes when the last one
  ends, so the same call reaches both platforms.
- **The register is not consulted.** The result has one delivery per platform, with the channel in
  place of an installation id, and nothing is removed from the register on failure.
- **A broadcast updates or ends; it cannot start.** Start by push with
  `LiveActivityOptions.Channel` — iOS gets it as `input-push-channel` — or in the app.
- **A channel belongs to one APNs environment.** `IPushChannels` and `BroadcastLiveActivityAsync`
  take it from `ApplePushOptions.Environment`, or from the call when that is `PerInstallation`.
- **The storage policy is fixed when the channel is made**: `None` for frequent updates, which gets
  a higher budget; `MostRecent` keeps the latest message for a device that was offline, for at most
  eight hours.

**Channel or token?** A channel when everyone sees the same content: a competition's leaders, a
match score. A token when it is personal: *your* start time, *your* result. An activity on a channel
has no token of its own, so nothing personal can be sent to it.

### Widgets

`RefreshWidgetsAsync` takes one of two roads per installation:

- **An installation with a widget token** — an iOS 26 app built with `SpineWidgetsPush` — gets a
  `widgets` push addressed to that token (`apns-push-type: widgets`, topic
  `<bundle id>.push-type.widgets`, `{"aps":{"content-changed":true}}`). WidgetKit reloads the widgets
  itself; the app does not run. The push cannot name a kind: it reloads every widget the extension
  has, and `kind` is ignored.
- **Every other installation** gets a silent push on Apple and a data message on Android, and the app
  rebuilds its widgets when it wakes — best effort: iOS throttles silent pushes, drops them after a
  force quit, and never delivers them to the simulator.

A widget token APNs rejects does not remove the installation. It says nothing about the device token,
and the app registers a new widget token at its next launch.

### Results

`PushResult` lists one `PushDelivery` per installation: `Sent`, `Invalid`, `Throttled` or `Failed`
with the service's own reason. An installation reported `Invalid` — a dead token, an unregistered
device, a 410 — is removed from the register on the way out, so the same device is not retried.

---

## The register

```csharp
public interface IPushInstallationStore
{
    Task UpsertAsync(PushInstallation installation, CancellationToken ct = default);
    Task<PushInstallation?> GetAsync(string id, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    IAsyncEnumerable<PushInstallation> QueryAsync(PushTagExpression expression, IReadOnlyCollection<PushPlatform>? platforms = null, CancellationToken ct = default);
    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken ct = default);
    Task<int> InvalidateAsync(string handle, CancellationToken ct = default);
}
```

`QueryAsync` skips expired registrations but does not remove them; removal is `PruneAsync`'s job, so
a read never has a side effect. `InMemoryPushInstallationStore` is the shape to copy — an
implementation over Cosmos DB, SQL or Table Storage is about a hundred lines.

---

## Registration endpoints

The app registers itself over HTTP. Both hosts serve the same contract:

```
PUT    {prefix}/installations/{id}
DELETE {prefix}/installations/{id}
```

**ASP.NET Core:**

```csharp
app.MapSpinePush("/push");
```

**Azure Functions, isolated worker:**

```csharp
[Function("SpinePush")]
public Task<IResult> Push([HttpTrigger(AuthorizationLevel.Anonymous, "put", "delete",
    Route = "push/installations/{id}")] HttpRequest request, CancellationToken ct)
    => SpinePushEndpoints.HandleAsync(request, ct);
```

A `PUT` whose body disagrees with the path, or that carries no handle, is a 400. `Authenticate`
saying no is a 401. `UpdatedAt` is stamped by the server, not read from the body: a device with a
wrong clock must not look freshly registered, since that is what `PruneAsync` goes by.

---

## Not in v1

| | Where it went |
|---|---|
| Windows (WNS via Entra) | v2 |
| Azure Table Storage register | With Orientera's backend |
| An Azure Notification Hubs transport | v3, if anyone wants one |
