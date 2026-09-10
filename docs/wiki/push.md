# Push (client)

`Plugin.Maui.Spine.Push` is the app half of Spine.Push: permission, tokens, tags, and the handler
that sees every message. The backend half is [`Plugin.Maui.Spine.Server`](push-server.md); the two
share the contracts in `Plugin.Maui.Spine.Common`.

It covers iOS, Android and Mac Catalyst. On Mac Catalyst remote push needs a provisioning profile — see [What each platform needs](#what-each-platform-needs) — and without one the app gets local notifications only. Windows is not covered yet.

---

## Run the sample first

`samples/MauiSpinePushSampleApp` is the shortest path to seeing this work. It shows the whole client
API on six pages — status and tokens, tags, a form that asks the server to send, a log of everything
the handler received, a Live Activity, and local notifications — and `samples/MauiSpinePushSampleApp.Server` is a Minimal
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

## Local notifications

The other half of notifying: the ones the device shows on its own, with no server involved.

```csharp
var local = services.GetRequiredService<ILocalNotificationService>();

await local.SyncAsync(
[
    new LocalNotification
    {
        Id = $"start:{competition.Id}",
        At = start.AddMinutes(-90),
        Title = "Dags att åka",
        Body = $"Start {start:HH:mm} i {competition.Name}.",
        Route = $"competition/{competition.Id}",
        Channel = "reminders",
    },
]);
```

`SyncAsync` takes the **whole plan** and makes the device equal it: anything not in the list is
cancelled. Re-planning is therefore idempotent — build the plan again whenever the data behind it
changes, and something that moved or stopped being true stops notifying instead of firing from a
stale schedule. `PendingAsync` reads back what is still to come, `CancelAllAsync` clears it, and
`IsSupported` is false where the platform has nothing to offer, so an app can say so rather than
pretend.

Give each notification an id derived from what it is about rather than a generated one. It is what
makes the plan replace rather than stack, and on Android it is also the notification's identity on
screen.

**Everything else is shared with push.** There is one notification permission per app on both
platforms, so `IPushService.RequestPermissionAsync` is the only place the user is asked. The channels
are the ones `AddChannel` created. And an opened local notification reaches the same
`IPushHandler.OnOpenedAsync`, carrying the same `spine.route`, so navigation does not need to know
which half sent it:

```csharp
public Task OnOpenedAsync(PushMessage message, string? action)
{
    if (message.IsLocal) log.Note("scheduled by us");
    return Navigate(message.Route);
}
```

`PushMessage.IsLocal` is there for logging and for the rare case an app wants to treat them
differently. A handler that does not care never has to look.

### Choosing which half sends what

An app that both schedules locally and receives push has to decide what each covers, or the user
gets the same thing twice. The framework cannot make that call — only the app knows which of its own
notifications the backend also sends — but `IPushService.IsRegistered` is the signal to make it with:

```csharp
var plan = Planner.Plan(state);

// Registered means the backend reaches this device, so leave out what it delivers. Not registered
// makes local the fallback for everything.
if (push.IsRegistered) plan = [.. plan.Where(n => !PushedKinds.Contains(n.Kind))];

await local.SyncAsync(plan);
```

The rule of thumb: anything only the device can work out — a time relative to the user's own plans,
something computed from data already on the phone — belongs locally. Anything only the server
notices belongs in push.

### An app that only notifies locally

Local notifications need no backend, no Firebase and no push entitlement. Leave `Backend` unset and
Spine skips the whole remote half: no APNs registration, no token, nothing in the log about a
`google-services.json` that is not there.

```xml
<SpinePushRemote>false</SpinePushRemote>
```

That property drops what only remote push needs from the build: the `google-services.json`
requirement on Android and `aps-environment` on Apple. The Firebase dependency itself still comes
with the package, so Android's `SupportedOSPlatformVersion` floor of 23 stays. `SpinePushEnabled=false`
still means neither half.

### What the platforms do

| | |
|---|---|
| iOS | Fires whether or not the app is running. The foreground presentation goes through `OnReceivedAsync`, exactly as a push does. Apple keeps 64 pending notifications per app; a larger plan is cut to the nearest 64, and Spine logs when it is. |
| Android | An inexact alarm per notification: it may arrive a few minutes late in doze, which is the price of not needing `SCHEDULE_EXACT_ALARM`. The plan is written down, so an alarm from an earlier run can still be cancelled, and it is booked again after a reboot. `OnReceivedAsync` is asked only in the foreground, so the two platforms behave alike. |
| Mac Catalyst | Like iOS: the same code. Local notifications work with or without a provisioning profile. |
| Windows | `IsSupported` is false, as it is for push. |

An instant that has already passed is dropped rather than fired late, on both platforms.

---

## Buttons, pictures and sound

What a notification can carry beyond its two lines. All three split between the platforms in ways
worth knowing before promising them, so each part says where.

### Buttons

Declare the button sets once, like channels, and let a notification name one:

```csharp
builder.UseSpinePush(push =>
{
    push.AddCategory("entry",
        new PushAction("enter", "Anmäl mig") { OpensApp = false },
        new PushAction("show", "Visa tävlingen"));

    push.AddCategory("chat", new PushAction("reply", "Svara") { Reply = "Skriv ett svar" });
});
```

```csharp
await local.SyncAsync([new LocalNotification { …, Category = "entry" }]);          // on the device
await sender.SendAsync(target, new PushNotification { …, Category = "entry" });   // from the server
```

Where a tap lands depends on the button, not on how the notification arrived:

| Button | Reaches | |
|---|---|---|
| `OpensApp` (the default) | `OnOpenedAsync(message, action)` | On the main thread with the app in front, so navigating is safe. |
| `OpensApp = false` | `OnActionAsync(message, action, null)` | In the app's process without bringing it forward — the app may not have been running. For work, not navigation. |
| `Reply = "…"` | `OnActionAsync(message, action, text)` | What the user typed. A reply never opens the app. |

`OnActionAsync` has a default that does nothing, so a handler without such buttons implements
nothing. The platform keeps the process alive until it returns — about thirty seconds on iOS, ten on
Android — and the notification is gone afterwards.

Why the declaration: iOS wants every button set registered **at launch** and shows no buttons for a
category it has not been told about, without a word. Android builds buttons per notification. Declaring
them in the options serves both; Spine registers them with iOS beside the notification delegate and
reads them on Android when it draws the notification — and logs when a notification names a category
that was never declared.

The rest differs in small ways: iOS shows up to four buttons when the notification is expanded,
Android three. `Destructive` draws red on Apple and like any other button on Android.

### Pictures

```csharp
new LocalNotification { …, Image = Path.Combine(FileSystem.CacheDirectory, "map.png") }  // a file
new PushNotification { …, Image = new Uri("https://example.com/map.png") }             // https only
```

| | Local | Push |
|---|---|---|
| iOS | Shown. Spine hands iOS a **copy**: iOS moves an attachment's file into its own store, so the app's file would otherwise disappear. | Needs the Notification Service Extension — see below. |
| Android | Shown, as `BigPictureStyle` with a thumbnail when collapsed. | Shown. Spine draws the notification itself and fetches the picture first, within FCM's time for the message. |

The rule everywhere is that **the notification always arrives**. A picture that cannot be fetched or
decoded, or that takes too long, leaves the text as it was, and the reason goes to the log — logcat
under `Spine.Push` on Android, `SpineNotificationService` in the device log on iOS. The server refuses
an `http` image outright, since App Transport Security would drop it silently on the device.

#### The Notification Service Extension

A pushed picture on iOS has to be fetched by an extension that iOS runs before showing the
notification. Spine builds it — swiftc, no Xcode project, the same way as the widget extension — when
the app asks for it:

```xml
<SpinePushImages>true</SpinePushImages>
```

It is off by default because it is a bundle of its own: device builds need an App ID
`<ApplicationId>.SpineNotificationService` and a development profile for it, exactly like the widget
extension, and the build stops with the details when there is none. Name a specific profile with
`SpinePushImagesCodesignProvision`. Simulator builds need nothing. The server sets
`mutable-content: 1` only when a notification has a picture, so the extension never runs otherwise.

### Sound

Here the halves are furthest apart, and Spine does not pretend otherwise.

- **Apple — per notification.** `LocalNotification.Sound` names a file in the app bundle (`.caf`,
  `.wav` or `.aiff`, under thirty seconds); unset is the system sound. From the server,
  `PushNotification.Sound` becomes `aps.sound`.
- **Android — per channel, for good.** `AddChannel("chime", "Med ljud", sound: "ding")` plays
  `Platforms/Android/Resources/raw/ding.*` for everything posted to it. Android keeps the sound a
  channel was *created* with: changing it later does nothing on a device that already has the
  channel, so a new sound means a new channel id. `PushNotification.Sound` does not apply here; pick
  the channel instead. A sound that is not in `raw` is logged at startup, since Android would quietly
  fall back to the default and keep that.

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
5. **Mac Catalyst:** set `EnableCodeSigning=true` even in Debug, or no permission prompt appears. Remote push also needs a provisioning profile that grants it, named with `CodesignProvision`: the push entitlement is restricted on the Mac, and an app signed with it but without such a profile does not launch. So Spine writes `aps-environment` for Catalyst only when a profile is named. Without one the app gets local notifications, and skips the APNs registration it could not complete.

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

Nor does `simctl push` run a Notification Service Extension. It hands the notification straight to
SpringBoard, so the step where iOS would start the extension never happens: a payload with
`mutable-content: 1` and `spine.image` arrives, but without its picture. That is the tool, not the
extension. A push that really goes through APNs does run it, in the simulator too: on Apple silicon
the simulator has a real sandbox token, so a backend with an APNs key can reach it, and the device log
shows `SpineNotificationService` handing back the notification with its attachment.

To see buttons and pictures at all, expand a **banner on an unlocked screen** — pull it down. On the
lock screen, and in Notification Center pulled down over it, a tap only hints at a swipe and the
notification stays collapsed, without its picture or its buttons.

It does issue a device token on Apple silicon, and registration against a local backend works from
it, so everything up to the actual APNs delivery can be exercised without a phone.

On Android, Firebase Console → Messaging → "Send test message" against a token reaches the service,
and the data keys go under "Additional options". An emulator with a Google Play image works.

---

## Not in v1

| | Where it went |
|---|---|
| Windows (WNS via Entra) | v2 |
| Live Activity broadcast channels | v2 |
| Devices without Google Play (HMS) | v3 |
