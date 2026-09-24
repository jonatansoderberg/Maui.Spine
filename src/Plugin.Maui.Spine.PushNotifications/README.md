# Plugin.Maui.Spine.PushNotifications

Push and local notifications for .NET MAUI: permission, APNs and FCM tokens, tags, one handler that sees every message, local scheduling with actions, and an optional Notification Service Extension that shows images in pushed notifications on iOS. The server half is `Plugin.Maui.Spine.Server`.

```bash
dotnet add package Plugin.Maui.Spine.PushNotifications
```

```csharp
builder
    .UseSpine(options => options.AddAssembly(typeof(MauiProgram).Assembly))
    .UseSpinePushNotifications(o =>
    {
        o.Backend = new Uri("https://api.example.com/push/");
        o.Permission = PushPermission.WhenAsked;
        o.AddChannel("competitions", "Competitions");
        o.UseHandler<MyPushHandler>();
    });
```

`UseSpine()` registers the package on its own; `UseSpinePushNotifications` is there for the options and works before or after it. Without Spine, the call is required.

```csharp
// Platforms/iOS/Program.cs — before UIApplication.Main, so the delegate methods exist when UIKit looks
SpinePushNotifications.Install();
```

```csharp
public sealed class MyPushHandler(INavigationService navigation) : IPushNotificationHandler
{
    public Task<PushPresentation> OnReceivedAsync(PushMessage message, PushContext context) =>
        Task.FromResult(context.IsForeground
            ? PushPresentation.None
            : PushPresentation.Banner | PushPresentation.Sound | PushPresentation.List);

    public async Task OnOpenedAsync(PushMessage message, string? action)
    {
        if (message.Route is "settings") await navigation.NavigateToAsync<SettingsPage>();
    }
}
```

```csharp
await push.RequestPermissionAsync();
await push.SetTagsAsync(["kind:results-published", "competition:59691"]);
```

`ILocalNotificationService` schedules local notifications through the same permission and the same handler.

Build properties: `SpinePushNotificationsEnabled`, `SpinePushNotificationsRemote` (false keeps local notifications and drops Firebase and the APNs entitlement), `SpinePushNotificationsImages` (adds the image extension on iOS), `SpinePushNotificationsEnvironment` (`development` or `production` for APNs).

Android needs `google-services.json` from a Firebase project and `SupportedOSPlatformVersion` 23 or later. iOS needs an App ID with Push Notifications and, for device builds, a provisioning profile.

Platforms: Android, iOS, Mac Catalyst (remote push needs a provisioning profile there). Windows gets no-op services.

## Documentation

- [Push (client)](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications.md)
- [Push (server)](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications-server.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
