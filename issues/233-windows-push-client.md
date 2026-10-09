# Issue #233 — Spine.Push: Windows client (PushNotificationManager, Entra remote id)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/233
**Branch:** issue/233-windows-push-client
**Status:** In Progress

## Plan

The server sends through WNS since #232: alerts as `wns/toast` that WNS draws itself, with the Spine keys in the toast's `launch` argument (`key=value;`, `%`/`;`/`=` percent-encoded), and silent messages as `wns/raw` with the data as one JSON object. The client is the missing half. Design: `docs/proposals/spine-push.md` §7.3 and §9.3.

Nobody can run Windows here, and there is no Entra registration, no WNS credentials and no packaged test app. So the work is code, docs and server-side tests, compiled for `net10.0-windows10.0.19041.0` with `EnableWindowsTargeting` from macOS. Everything that needs Windows goes to #501.

1. **The WNS wire format in one place.** `WnsPayload` in `Plugin.Maui.Spine.Common` (internal, visible to Server and PushNotifications) writes and reads the toast arguments and the raw JSON. The server's `PushPayloads.Wns`/`WnsSilent` use it to write; the Windows client uses it to read. Tests on the server side prove the round trip the client depends on.
2. **Options:** `SpinePushNotificationsOptions.Windows.RemoteId` — the Object ID of the Entra app's service principal, which `CreateChannelAsync` takes. Same nested shape as `SpineOptions.Windows`.
3. **`WindowsPushPlatform : IPushPlatform`:**
   - `Status = Unsupported` with a named reason when `Windows.RemoteId` is missing, the process is elevated (`Environment.IsPrivilegedProcess`), or `PushNotificationManager.IsSupported()` is false (self-contained Windows App SDK — .NET MAUI's default for `WindowsPackageType=None` — and the other cases Microsoft lists). Otherwise `AppNotificationManager.Default.Setting`: `Enabled` → `Authorized`, `Disabled*` → `Denied`. Windows has no permission prompt.
   - `PushReceived` subscribed before `PushNotificationManager.Default.Register()`, and `Register()` before `AppInstance.GetActivatedEventArgs()` (the Windows App SDK's ordering).
   - A fresh channel from `CreateChannelAsync(remoteId)` at every launch, again on foreground when it expires within a day; the channel URI (`OriginalString`, so WNS's escaping survives) is the handle. `InProgressRetry` progress and failures are logged with the HRESULT.
   - Raw pushes: payload JSON → `PushMessage` → `HandleInternallyAsync` → handler, inside a deferral. A raw push launched in the background (`ExtendedActivationKind.Push`, packaged only) takes the same path.
4. **Opened toasts:** `AppNotificationManager.NotificationInvoked` and `Register()` at launch; a cold start from a toast through the activation arguments; activations redirected by Spine's single-instance handling through `AppInstance.Activated`. Arguments are read with `WnsPayload`, `spine.action` names a button; buttons declared with `RunsInBackground` go to `OnActionAsync`, the rest to `OnOpenedAsync` with the window brought forward.
5. **Local notifications:** scheduled toasts (`ToastNotifier.AddToSchedule`) for packaged apps, built from the same arguments as a push toast, with the category's buttons and reply fields. An unpackaged app has no package identity to schedule under: `IsSupported = false`, and the first call logs why.
6. **Build check:** a warning when a Windows app with remote push is self-contained, naming `WindowsAppSDKSelfContained=false` as the fix.
7. **Sample:** the push sample's Windows head reads `WindowsRemoteId` from `appsettings.json` and turns off self-contained so it can get push.
8. **Docs:** client wiki (setup, Entra, unpackaged vs MSIX with the PFN mapping, manifest entries, behaviour table), server wiki, package README, the notifications skill, proposal §9.3 corrected.

## Changes

- `Plugin.Maui.Spine.Common/Push/WnsPayload.cs` (internal): `WriteArguments`/`ReadArguments` for the toast `launch` and button arguments, `WriteRaw`/`ReadRaw` for the raw body, `Action = "spine.action"`. Common now lets `Plugin.Maui.Spine.Server` see its internals for it.
- `PushPayloads.Wns`/`WnsSilent` write through `WnsPayload`; the private `WnsArguments` and the inline JSON writer are gone. Output unchanged (the existing payload tests pass as they were).
- `SpinePushNotificationsOptions.Windows.RemoteId` (`WindowsPushOptions`).
- `Platforms/Windows/WindowsPushPlatform.cs`: status and the unsupported reason, `PushReceived` + `Register()`, `CreateChannelAsync` with retry progress and HRESULT logging, the channel URI as handle, raw pushes through the shared handler path under a deferral, a raw alert drawn with `AppNotificationManager.Show`.
- `Platforms/Windows/SpinePushNotificationsExtensions.Windows.cs`: `OnLaunched` registers toasts and push in the SDK's order, reads the activation (`AppNotification`, classic toast activation, `Push`), subscribes to `AppInstance.Activated` for redirected activations, gets a channel and registers; `OnActivated` tracks the foreground and renews/refreshes. Tapped toasts route to `OnOpenedAsync` (window activated) or `OnActionAsync` for declared background buttons.
- `Platforms/Windows/WindowsNotifications.cs`: toast XML from a data bag — title, body, hero picture (https or a local file), the category's buttons and reply inputs, each button carrying the toast's arguments plus `spine.action`; reading a tapped toast back.
- `Platforms/Windows/WindowsLocalNotifications.cs`: `SyncAsync`/`PendingAsync`/`CancelAllAsync` with scheduled toasts for packaged apps; unpackaged is `IsSupported = false`, logged once.
- The package references `Microsoft.WindowsAppSDK` for the Windows TFM (same pinned version as `Plugin.Maui.Spine`).
- `Plugin.Maui.Spine.PushNotifications.targets`: `_SpinePushNotificationsWindowsCheck` warns when a Windows app with remote push is self-contained.
- Push sample: `WindowsAppSDKSelfContained=false` on Windows, `WindowsRemoteId` in `appsettings.json` (empty) read into `options.Windows.RemoteId`.
- Tests: `WnsPayloadTests` (13 test cases) — the app reads back the server's toast arguments and raw body, buttons, escaping round trip, non-string raw values, a non-object body refused, a channel URI through the registration JSON.
- Docs: client wiki (Windows section: Entra steps, remote id, self-contained, elevated, unpackaged vs packaged table, PFN mapping mail, manifest activators, single instance; handler, status, local, buttons, pictures, sound, testing), server wiki, package README, csproj description, `spine-notifications` skill, proposal §7.3/§9.3 (remote id is the service principal's Object ID; mapping mail needs PFN, AppId and ObjectId).

## Verification

- `dotnet build src/Plugin.Maui.Spine.PushNotifications/Plugin.Maui.Spine.PushNotifications.csproj -f net10.0-windows10.0.19041.0 -p:EnableWindowsTargeting=true "-p:SpineMauiTargetFrameworks=net10.0-windows10.0.19041.0" -p:AppxGeneratePriEnabled=false -p:EnableMsixTooling=false --no-incremental` on macOS: 0 warnings, 0 errors. The API shapes (`PushNotificationCreateChannelStatus` fields, `ExtendedError`, `ExpirationTime`, `AppNotificationActivatedEventArgs.Argument`/`UserInput`, `AppNotificationSetting`) are checked by that compile against Windows App SDK 1.8.260804001.
- The push project for android, ios and maccatalyst; the server; the sample server; the push sample for Android: all build.
- `dotnet test tests/Plugin.Maui.Spine.Server.Tests`: 243 passed, 12 skipped (Azurite). `Plugin.Maui.Spine.BackgroundTasks.Tests`: 20 passed.
- The push sample for Windows does **not** build on macOS: the WinUI XAML compiler is a Windows executable ("XamlCompiler output file … was not created"). Every library it references compiles. The self-contained warning fires with `-p:WindowsAppSDKSelfContained=true` and not without.
- Nothing ran on Windows. The checklist is in #501.

## Decisions

- **Remote id is the service principal's Object ID** (Microsoft's quickstart), not the client id the API reference's wording suggests. The channel failure log names both the expected id and the other causes.
- **Self-contained is a build warning, not an error.** An app may want local notifications only, or push only in its packaged flavour; `SpinePushNotificationsRemote=false` silences it.
- **Unsupported, with the reason in the log, rather than a silent no-op.** Missing remote id, elevated process and `IsSupported() == false` each produce their own message; the remote-id message is skipped for apps with no backend (local-only).
- **No permission prompt on Windows.** `Status` is the Settings switch; `NotDetermined` never occurs.
- **The handle is not persisted**, so the first `ChannelAsync` in a process always asks WNS — Microsoft's "fresh channel every launch" — and the fingerprint decides whether the backend hears about it.
- **Local notifications need package identity.** The Windows App SDK can show a toast but not schedule one, and `ToastNotificationManager.CreateToastNotifier()` needs a package. Faking an AUMID for unpackaged apps would hang on how the Windows App SDK registers its own, which cannot be checked here.
- **Arguments read from `Argument`, not `Arguments`.** Spine decodes the raw string with the same code the server encodes with, instead of relying on the SDK's own decoding matching it.
- **Server toasts get no buttons from categories.** The server does not know the app's categories; the docs show how to add a button with `PushNotification.Windows` that routes back to the declared one.
- **Proposal edits stay in Swedish.** `docs/proposals/spine-push.md` is a Swedish document; the corrected lines in §7.3/§9.3 match it rather than switching language mid-list.
