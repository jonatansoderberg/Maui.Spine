---
name: spine-notifications
description: Add push and local notifications with Plugin.Maui.Spine.PushNotifications and the Plugin.Maui.Spine.Server backend — registration, permission, tags, the handler that sees every message, local scheduling with SyncAsync, channels, categories with buttons, pictures and sounds, and the server's AddSpinePushNotifications / MapSpinePushNotifications / IPushSender. Use when an app or its backend needs notifications. Invoke as /spine-notifications.
---

You are adding notifications to an app built on Spine, or to its backend. The client package is **Plugin.Maui.Spine.PushNotifications** (push *and* local notifications, one permission, one handler); the server package is **Plugin.Maui.Spine.Server** (a device register, tag expressions, and transports straight to APNs, FCM and WNS — no hosted hub). Full docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications.md and https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications-server.md.

Check `/spine-setup` §7 for the project setup (Android minSdk 23, `google-services.json`, `SpinePushNotifications.Install()` in `Program.cs`, `aps-environment`).

---

## Client

### Register

```csharp
builder
    .UseSpine(…)
    .UseSpinePushNotifications(o =>        // UseSpine registers the package; this call sets the options (before or after UseSpine)
    {
        o.Backend = new Uri("https://api.example.com/push/");   // the server's MapSpinePushNotifications prefix; unset = local only
        o.Permission = PushPermission.WhenAsked;                // ask when the app calls RequestPermissionAsync
        o.AddChannel("news", "News");                           // Android channels; iOS ignores
        o.AddChannel("alerts", "Alerts", PushChannelImportance.High);
        o.AddCategory("order",                                  // buttons on a notification
            new PushAction("open", "Open"),
            new PushAction("ack", "Acknowledge") { OpensApp = false },
            new PushAction("reply", "Reply") { Reply = "Write something" });
        o.UseHandler<MyPushHandler>();
    });
```

### The handler

Resolved from DI per message; both push and local notifications reach it.

```csharp
public sealed class MyPushHandler(INavigationService navigation) : IPushNotificationHandler
{
    public Task<PushPresentation> OnReceivedAsync(PushMessage message, PushContext context) =>
        Task.FromResult(context.IsForeground
            ? PushPresentation.None                                              // show it in the app instead
            : PushPresentation.Banner | PushPresentation.Sound | PushPresentation.List);

    public async Task OnOpenedAsync(PushMessage message, string? action)          // main thread, host ready
    {
        if (message.Route is { } route) await Navigate(route);                  // the app maps routes to typed navigation
    }

    public Task OnActionAsync(PushMessage message, string action, string? text) => …;   // a button that does not open the app
}
```

`PushKind.Widget` and `PushKind.LiveActivity` messages are handled by Spine before the handler sees them (widget refresh, activity start/update/end).

### Permission, tags, registration

```csharp
var push = services.GetRequiredService<IPushNotificationService>();
await push.RequestPermissionAsync();                              // the one permission for push and local
await push.SetTagsAsync(["kind:news", "team:red"]);                // replaces; AddTagsAsync / RemoveTagsAsync edit
await push.RefreshAsync();                                        // re-register (token rotated, tags changed)
push.IsRegistered;                                                // the backend reaches this device
```

Tags are opaque strings the server matches with expressions (`kind:news && !muted`). Put the user in a tag (`user:123`) and let the server's `AllowTags` keep clients from claiming others.

### Local notifications

```csharp
var local = services.GetRequiredService<ILocalNotificationService>();

await local.SyncAsync(
[
    new LocalNotification
    {
        Id = $"reminder:{item.Id}",           // derived from the subject, so re-planning replaces instead of stacking
        At = item.DueAt.AddMinutes(-30),
        Title = "Due soon",
        Body = $"{item.Name} is due at {item.DueAt:HH:mm}.",
        Route = $"item/{item.Id}",
        Channel = "reminders",
        Category = "order",                    // buttons, from AddCategory
        Image = pathToPng,                     // a picture, on iOS and Android
        Sound = "ding.wav",                    // iOS per notification; Android per channel
    },
]);
```

`SyncAsync` takes the **whole plan**: whatever is not in the list is cancelled, so rebuild the plan whenever the data changes. `PendingAsync` reads it back, `CancelAllAsync` clears it, `IsSupported` is false where the platform cannot. A local-only app leaves `Backend` unset and sets `<SpinePushNotificationsRemote>false</SpinePushNotificationsRemote>`.

When both halves exist, decide per kind which one sends: `if (push.IsRegistered) plan = plan.Where(n => !PushedKinds.Contains(n.Kind))`.

### Testing on the simulator

`xcrun simctl push <udid> <bundle-id> payload.json` delivers a push without a server (no Notification Service Extension runs, so no picture). Banners show only while the device is unlocked and the app is in the background.

## Server

```csharp
builder.Services.AddSpinePushNotifications(o =>
{
    o.Apple(a => { a.TeamId = cfg["Push:Apple:TeamId"]; a.KeyId = cfg["Push:Apple:KeyId"]; a.PrivateKey = cfg["Push:Apple:PrivateKey"]; a.BundleId = "com.example.app"; });
    o.Android(f => f.ServiceAccountJson = cfg["Push:Fcm:ServiceAccount"]);
    o.UseAzureTableStore(cfg["Storage"]);      // or UseInMemoryStore() / UseStore(myStore)
    o.AllowTags = (installation, tags) => tags.Where(t => !t.StartsWith("user:") || t == $"user:{installation.UserId}");
    o.Authenticate = request => ValueTask.FromResult(request.Headers.Authorization == expected);
});

app.MapSpinePushNotifications();              // /push/installations …; the client's Backend points here
```

```csharp
await sender.SendAsync(PushTarget.Tags("kind:news && !muted"), new PushNotification
{
    Title = "Results published", Body = "Your class is in.", Route = "results/59691", Channel = "news",
});
await sender.SendSilentAsync(PushTarget.User("123"), new Dictionary<string, string> { ["refresh"] = "orders" });
await sender.RefreshWidgetsAsync(PushTarget.All, kind: "next-event");
await sender.UpdateLiveActivityAsync(PushTarget.Installation(id), kind: "delivery:42", layout, LiveActivityEvent.Update);
```

Targets: `PushTarget.Tags(expr)`, `.User(id)`, `.Installation(id)`, `.All`. One call reaches every platform the target matched; the app's handler sees the same message on each. APNs refuses payloads over 4 KB.

Works in ASP.NET Core Minimal APIs and in Azure Functions isolated workers (the same `HttpRequest`).

## Do / Don't

- Do ask for permission behind a user action, and only once per app (push and local share it).
- Do derive notification and activity ids from the subject; never random ids.
- Do send tokens up at every launch and foreground; they rotate.
- Don't leave out `UseSpinePushNotifications` in an app without `UseSpine`: nothing else registers it there.
- Don't put `google-services.json` with real keys in a public repo; the sample's checked-in one is a placeholder.
- Don't expect a Mac Catalyst debug build to get remote push without a provisioning profile named in `CodesignProvision`; it gets local notifications only.

## Documentation

- Client: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications.md
- Server: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications-server.md
- Sample app and server: https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples/MauiSpinePushNotificationsSampleApp
