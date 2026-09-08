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
        a.BundleId   = "com.companyname.orientera";
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

### Widgets

`RefreshWidgetsAsync` sends a silent push on Apple and a data message on Android, and the app
rebuilds its widgets when it wakes. That is best effort by design. iOS 26's dedicated `widgets` push
type needs the widget extension built as a `pushHandler`, which is not in v1.

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
| iOS 26 widget push (`WidgetPushHandler`) | v2 |
| Live Activity broadcast channels (iOS 18) | v2 |
| An Azure Notification Hubs transport | v3, if anyone wants one |
