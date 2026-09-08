# Push (client)

`Plugin.Maui.Spine.Push` is the app half of Spine.Push: permission, tokens, tags, and the handler
that sees every message. The backend half is [`Plugin.Maui.Spine.Server`](push-server.md); the two
share the contracts in `Plugin.Maui.Spine.Common`.

v1 covers iOS, Mac Catalyst and Android. Windows is v2.

---

## Run the sample first

`samples/MauiSpinePushSampleApp` is the shortest path to seeing this work. It shows the whole client
API on five pages — status and tokens, tags, a form that asks the server to send, a log of everything
the handler received, and a Live Activity — and `samples/MauiSpinePushSampleApp.Server` is a Minimal
API on `Plugin.Maui.Spine.Server` with an in-memory register.

```bash
cd samples/MauiSpinePushSampleApp.Server && dotnet run     # then run the app
adb reverse tcp:5100 tcp:5100                              # Android only, once per device
```

The app reaches the server on `localhost:5100` from both platforms: the iOS simulator shares the
Mac's network, and Android gets there through `adb reverse` — emulator or a phone on a cable alike.
Do not reach for `10.0.2.2`. It is the qemu gateway on the emulator's `eth0`, but app traffic goes
over `wlan0`, where the same address is the emulated router and never reaches the host; the send
fails with a bare `Connection failure` that says nothing about why.

The server starts without any credentials: it can register devices and answer `/installations`
straight away, and says so when a send reaches nobody because no platform is configured. Add an APNs
key or a Firebase service account with `dotnet user-secrets` when you want messages to actually go
out. `send.http` has the same calls for anyone who prefers the editor.

---

## Setup

```csharp
builder
    .UseSpine(…)
    .UseSpineWidgets()          // optional; when present, Live Activity push tokens are sent up
    .UseSpinePush(o =>
    {
        o.Backend = new Uri("https://api.example.com/push/");
        o.Permission = PushPermission.WhenAsked;
        o.AddChannel("competitions", "Tävlingar");
        o.UseHandler<MyPushHandler>();
    });
```

Call `UseSpinePush` **after** `UseSpineWidgets`, so it can see that Widgets is there and turn Live
Activity push tokens on.

### One line on Apple platforms

```csharp
// Platforms/iOS/Program.cs
static void Main(string[] args)
{
    SpinePush.Install();                                  // ← before UIApplication.Main
    UIApplication.Main(args, null, typeof(AppDelegate));
}
```

MAUI's lifecycle API has no hooks for the remote-notification delegate methods, so Spine adds them
to the `AppDelegate` class at runtime. It has to happen before `UIApplication.Main`, because UIKit
reads which callbacks the delegate implements when the delegate is assigned — which is inside
`UIApplication.Main`.

If the app already implements one of those methods, Spine leaves it alone, logs a warning naming the
selector, and lists it in `SpinePush.NotInstalled`. Call the matching forwarder from your own
implementation:

```csharp
[Export("application:didReceiveRemoteNotification:fetchCompletionHandler:")]
public void DidReceive(UIApplication app, NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler)
{
    SpinePush.Forward.DidReceive(userInfo, completionHandler);
}
```

---

## The handler

```csharp
public sealed class MyPushHandler(INavigationService navigation) : IPushHandler
{
    public Task<PushPresentation> OnReceivedAsync(PushMessage message, PushContext context) =>
        Task.FromResult(context.IsForeground
            ? PushPresentation.None          // show it in the app instead
            : PushPresentation.Banner | PushPresentation.Sound | PushPresentation.List);

    public async Task OnOpenedAsync(PushMessage message, string? action)
    {
        if (message.Route is "settings") await navigation.NavigateToAsync<SettingsPage>();
    }
}
```

The handler is resolved through DI for every message, so constructor injection works as in a view
model. Without a handler the system shows what it would have shown anyway:
`Banner | Sound | List`.

`OnOpenedAsync` runs on the main thread, and on a cold start after the Spine host exists, so
navigating from it is safe. `Route` is delivered as the sender wrote it — Spine does not resolve it
to a page, because `INavigationService` is generic and has no string lookup. The app decides what a
route means, the same way `IWidgetLinkHandler` handles a widget's link.

| `PushKind` | What Spine does before the handler sees it |
|---|---|
| `Alert` | Nothing. On Android the notification is drawn after the handler answers |
| `Silent` | Nothing |
| `Widget` | `IWidgetService.RefreshAsync(kind)`, when Widgets is installed |
| `LiveActivity` | Starts, updates or ends the activity through `ILiveActivityService` |

---

## Permission, tags, registration

```csharp
var push = services.GetRequiredService<IPushService>();

await push.RequestPermissionAsync();
await push.SetTagsAsync(["kind:results-published", "competition:59691"]);
```

`PushPermission` decides when the user is asked: `WhenAsked` (the default), `Provisional` — iOS's
quiet authorization, notifications arrive in the Notification Center and the user is asked to keep
them after seeing one — or `AtLaunch`.

Spine adds `platform:`, `os:` and `app:` tags of its own, so a sender can address a platform or a
version without the app doing anything.

Registration goes out at launch and on every foreground, but only when something actually changed —
token, tags, versions, Live Activity tokens — plus once every `Confirm` (15 minutes by default) so
the server can see the device is alive. The installation id lives in secure storage and survives
token changes and reinstalls of the same app.

`RefreshAsync` and the three tag methods answer with a `PushRegistrationResult`, so an app can tell
the cases apart instead of guessing at a `false`:

| | |
|---|---|
| `Sent` | The backend has it. |
| `Unchanged` | Nothing had changed and the confirm window had not run out. |
| `NoBackend` | No `Backend` is configured. |
| `NoToken` | The platform has not issued a token — read `IPushService.Token` to see. |
| `Failed` | The backend refused it, or could not be reached. |

Tags are kept locally whatever the answer, so a `NoToken` leaves the app subscribed to things the
server does not know about. Show the answer rather than swallowing it.

`RefreshAsync(force: true)` sends even when nothing changed. That is for the one case the
fingerprint cannot see: a register that lost the row — a recreated container, a restored backup, a
restarted dev server. Nothing on the wire tells a device it is no longer registered, so an app that
offers the user a "register again" needs this; without it the button does nothing until the confirm
window runs out.

---

## What each platform needs

### Apple

1. **App ID.** Certificates, Identifiers & Profiles → the app's identifier → tick **Push
   Notifications** → Save. Regenerate the provisioning profile afterwards; without it the signature
   has no `aps-environment` and registration fails with "no valid aps-environment".
2. **APNs key.** Keys → **+** → tick **Apple Push Notifications service** → download the `.p8`. It
   can only be downloaded once. Note the Key ID and the Team ID; one key covers every app in the
   team, both environments and all push types.
3. **Entitlements.** Spine's build contributes `aps-environment` — `development` in Debug,
   `production` otherwise, overridable with `SpinePushEnvironment`. An app that owns its own
   entitlements file keeps it, and the build then tells you which key to add.
4. **Environment matters.** A debug build on a device gets a sandbox token, TestFlight and the App
   Store get production tokens, and one never works against the other. The app reports which it got,
   so the server picks the right host.
5. **Mac Catalyst:** set `EnableCodeSigning=true` even in Debug, or no permission prompt appears.

### Android

Two things Spine's own floor does not cover:

- **minSdk 23.** Firebase Messaging's dependencies declare it, while Spine allows 21. The build tells
  you exactly what to add if you forget.
- **AndroidX versions.** Firebase and MAUI disagree about `LiveData.Core` and `Fragment`, so the
  package references the versions that satisfy both. Expect `NU1608` warnings about a violated upper
  bound; that is the normal state of a MAUI app with Firebase in it.

1. **Firebase project**, then an Android app in it whose package name is the app's `ApplicationId`.
2. **`google-services.json`** into `Platforms/Android/`, and in the csproj:
   ```xml
   <GoogleServicesJson Include="Platforms\Android\google-services.json"
                       Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'" />
   ```
   The build fails with a clear message when it is missing, rather than at runtime with one that
   says nothing about the file.
3. **Service account** for the server: Project settings → Service accounts → Generate new private
   key. That is the FCM HTTP v1 identity; the legacy server key is gone.
4. **Notification icon.** Add a monochrome drawable named `spine_push_icon`; without one Android
   falls back to the launcher icon, which it may draw as a white square.
5. **Channels** are created at startup from `AddChannel`. A message names one in `spine.channel`.

---

## Testing without a backend

`xcrun simctl push` delivers a payload to the simulator with no real token involved, which is the
way to check the handler, the message shape and the presentation:

```bash
cat > alert.json <<'JSON'
{
  "aps": { "alert": { "title": "Resultat klara", "body": "Gävle OK" }, "sound": "default" },
  "spine.kind": "alert",
  "spine.route": "competition/59691"
}
JSON

xcrun simctl push booted se.cosmomedia.orientera alert.json
```

What the simulator will not do is deliver silent (`content-available`) pushes — a normally declared
`didReceiveRemoteNotification:` implementation is not called either, so that is the simulator and
not your code. That needs a physical device with a profile and the push entitlement.

It does issue a device token on Apple silicon, and registration against a local backend works from
it, so everything up to the actual APNs delivery can be exercised without a phone.

On Android, Firebase Console → Messaging → "Send test message" against a token reaches the service,
and the data keys go under "Additional options". An emulator with a Google Play image works.

---

## Not in v1

| | Where it went |
|---|---|
| Windows (WNS via Entra) | v2 |
| iOS 26 widget push | v2; until then a silent push rebuilds the widgets |
| Live Activity broadcast channels | v2 |
| Devices without Google Play (HMS) | v3 |
