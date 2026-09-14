# Plugin.Maui.Spine.Server

The server half of Spine push notifications: a register of installations, tag expressions that address them, transports that send straight to APNs, FCM v1 and WNS, and the endpoints the app registers through. A plain `net10.0` library with a framework reference to ASP.NET Core, so a Minimal API and an Azure Functions isolated worker can both host it.

```bash
dotnet add package Plugin.Maui.Spine.Server
```

```csharp
builder.Services.AddSpinePushNotifications(options =>
{
    options.Apple(a =>
    {
        a.TeamId = cfg["Push:Apple:TeamId"];
        a.KeyId = cfg["Push:Apple:KeyId"];
        a.PrivateKey = cfg["Push:Apple:PrivateKey"];
        a.BundleId = "com.example.app";
    });
    options.Android(f => f.ServiceAccountJson = cfg["Push:Fcm:ServiceAccount"]);
    options.UseAzureTableStore(cfg["Storage"]);   // or UseInMemoryStore()
});

app.MapSpinePushNotifications();                  // /push/installations, /push/…
```

```csharp
await sender.SendAsync(
    PushTarget.Tags("club:okl && class:D21"),
    new PushNotification { Title = "Start list", Body = "D21 starts at 10:32" });
```

Live Activities, iOS 26 widget push and broadcast channels go through the same sender.

## Documentation

- [Push (server)](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications-server.md)
- [Push (client)](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
