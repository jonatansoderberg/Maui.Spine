# Issue #786 — Showcase: move the push sample into the Showcase, and its server with it

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/786
**Branch:** issue/786-push-sample-into-showcase
**Status:** In Progress

## Plan
Mapping (2026-11-05):
- The push app has six pages (Home, Tags sheet, Send, Log, Local, Live Activity), `PushLog`, `SamplePushHandler`, `SampleServer`, `WidgetContent`, three widgets (`sample`, `remote`, `picture`) and `StampTask`.
- The Showcase already covers widgets, Live Activities and background tasks locally; what it lacks is everything that goes through the server: push itself, a Live Activity updated by push, a widget fetched from the server, a task a push starts.
- The server is already the Showcase's backend. In Azure (`Sample:Hosted`) `/send`, `/channels` and `/installations` need a signed-in user and reach that user's devices only, which the Showcase's Sign-in page provides.

Placement (owner's choice): two new pages, Push notifications and Local notifications; the server parts go into the existing Live Activities, Widgets and Background tasks pages. The server is renamed `MauiSpineSampleApp.Server` and the push app is removed.

## Changes
- Server: `samples/MauiSpinePushNotificationsSampleApp.Server` renamed `samples/MauiSpineSampleApp.Server` (namespace, csproj, README, `Spine.slnx`); APNs bundle id `se.cosmomedia.spineshowcase` in `appsettings.Development.json`; the push and remote-widget strings in English.
- Showcase: `Plugin.Maui.Spine.PushNotifications` with its targets, `UseSpinePushNotifications` against the server's `/push/` (channels news, alerts, chime; category sample), `SpinePushNotifications.Install()` in `Platforms/iOS/Program.cs`, `ding.wav` and `sample_picture.png`, the Firebase file for the Showcase's package, remote push off on Windows, the Notification Service Extension and widget push in simulator builds.
- `Push/`: `PushLog`, `ShowcasePushHandler` (route `log` opens the push log), `PushServer` (the Sign-in page's `SampleApi` client, so Azure sees the user).
- Widgets: `push` (was the push app's `sample`), `remote`, `picture`.
- New pages: Push notifications (this installation, tags, send to yourself with options, buttons and replies, what the handler saw, a widget a push rebuilds) with a Push log subpage; Local notifications (a plan, not a queue; buttons, a picture and a sound).
- Existing pages: Live Activities (updated from the server; one push to every device on a channel), Widgets (fetched from the server; on a picture), Background tasks (started by a push).
- Removed `samples/MauiSpinePushNotificationsSampleApp`; docs (`push-notifications.md`, `widgets.md`, the AI and sign-in pages), README, skills, `LiveServer.cs` and comments point at the Showcase and its server.
- Apple Developer: Push Notifications with Broadcast Capability on the App ID `se.cosmomedia.spineshowcase`, the Spine Showcase Development profile made again and installed; the local user secret `Push:Apple:BundleId` set to the Showcase's.
- Azure: `Push__Apple__BundleId` on `spine-showcase-e41ddd` is now `se.cosmomedia.spineshowcase` (the other push keys were already there).
- Simulator: the old Spine Push app uninstalled.
- Firebase: a project of its own, Spine Showcase (`spine-showcase-56b63`, no Analytics or Gemini), with the Android app `se.cosmomedia.spineshowcase`. The repo keeps a placeholder for that package; the real `google-services.json` stays local under skip-worktree, as the push app's did.

## Decisions
- `UserSecretsId` stays `maui-spine-push-sample-server`, so the keys on the Mac follow the rename.
- The push app's `sample` widget becomes `push`: the Showcase has its own `sample`.
- `StampTask` is dropped: a push names the Showcase's `sample-sync` in `spine.task` instead.
- No gate on iOS device builds: the App ID has Push Notifications since 2026-11-05 and the development profile carries `aps-environment`.
- Remote push is off on Windows (`SpinePushNotificationsRemote=false`): WNS needs a framework-dependent Windows App SDK, which would change how the Showcase ships on Windows. Local notifications still work there.

## Verified
- iOS simulator (iPhone 17, iOS 26.4): the list rows, the Push notifications page (permission prompt, status), the Widgets page's new buttons, `xcrun simctl push` in the foreground (logged, answered None) and opened from the background (route `log` opens the Push log).
- Not verified: a real APNs token. The simulator's `apsd` was connected on 0 interfaces, so no token arrived and the server's register stayed empty; sends, tags, the server's Live Activity and channel, and the remote widget's fetch need a token or a device.
- An incremental iOS build after adding the push package kept the old `__entitlements` section without `aps-environment` (SpringBoard: "No valid 'aps-environment' entitlement string"); a clean build fixed it.
