# Issue #312 — Feature idea: Control Center controls and Quick Settings tiles from the widget vocabulary

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/312
**Branch:** issue/312-feature-idea-control-center-controls-and-quick-set
**Status:** Completed

## Plan

There is no study in `docs/proposals/` for this issue; the plan follows the issue and the architecture of Spine.Widgets (`docs/proposals/spine-widgets.md`, `docs/wiki/widgets.md`). Worked unattended overnight, so every choice is recorded under **Decisions**.

A control is a toggle or a button with a title, an icon and a state. It lives in `Plugin.Maui.Spine.Widgets`, next to widgets, because it needs the same native pieces: the extension compiled with `swiftc`, the App Group, the bridge framework and the in-process intent from #218 on iOS, and the receivers' process model on Android.

### 1. Contracts (`Plugin.Maui.Spine.Common/Core`)
- `[Control("kind")]` and `IControlProvider` with `GetStateAsync(ControlContext, CancellationToken)` and `OnActionAsync(ControlAction)`.
- `ControlState` (`Title`, `Icon`, `Symbol`, `IsOn`, `Status`, `Tint`) with `ControlState.Toggle(…)` and `ControlState.Button(…)`.
- `IControlService`: `IsSupported`, `Kinds`, `RefreshAsync(kind)` / `RefreshAsync<T>()` / `RefreshAllAsync()`, `RequestAddAsync(kind)`.
- `WidgetJson.Serialize(ControlState)`: the document the native side reads.

### 2. Build (`build/Plugin.Maui.Spine.Widgets.targets`, `spine-widgets-build.sh`)
- A `<SpineControl Include="kind" Type="Toggle|Button" DisplayName Description Icon />` item, the counterpart of `<SpineWidget>`. Every condition that required a widget accepts a control instead, so an app with only controls builds the extension too.
- iOS: the manifest gets a `controls` list; the generated bundle gets one `ControlWidget` per control in an iOS 18 nested bundle (`if #available`), so widgets keep working on iOS 17. Not on Mac Catalyst.
- Android: one `TileService` per control from fixed slots `SpineControlTile0..8`, with label, icon and `TOGGLEABLE_TILE` metadata in the generated manifest overlay.

### 3. iOS
- `SpineControlIntent.swift` (bridge + extension): `SpineControlToggleIntent` (`SetValueIntent`) and `SpineControlButtonIntent`, both `LiveActivityIntent` so iOS runs them in the app's process like the widget button. They record the tap in `actions.jsonl` with `"control": true` and wait for .NET, through the same `ActionLog`/`ActionCompletions` as #218. The toggle writes its new value into the control's document first, so Control Center shows it even when the intent ran in the extension.
- `SpineControls.swift` (extension only): a `ControlValueProvider` that reads `spine-widgets/controls/<kind>.json`, and the toggle and button configurations.
- Bridge: `reloadControls(kind:)`, `reloadAllControls()`.
- C#: the action drain routes control taps to the provider, writes the new state, reloads the control, then completes the intent.

### 4. Android
- `SpineControlTile` (`TileService`, API 24+): draws the stored state at `OnStartListening`, asks the provider for a fresh one, and runs `OnActionAsync` on a click in the app's process (optimistic flip first).
- `RequestAddAsync` uses `StatusBarManager.requestAddTileService` on Android 13+.

### 5. Wiring
- Controls refresh at launch, at backgrounding, in background runs and after a tap, like widgets.

### 6. Sample, docs, tests
- Showcase page "Control Center" (`Pages/ControlCenter/`) with two controls on the live-score demo: a "Goal alerts" toggle and a "Goal" button that scores for the followed team without opening the app.
- A "Controls" section in `docs/wiki/widgets.md`, the `/spine-widgets` skill, package README.
- Serialization tests in `tests/Plugin.Maui.Spine.Server.Tests` (where the widget JSON tests live).

## Open Questions

None that block v1; see **Decisions** and the follow-ups in the PR.

## Changes

- `Plugin.Maui.Spine.Common`: `ControlAttribute`, `IControlProvider` (`GetStateAsync`, `OnActionAsync`), `ControlContext`, `ControlAction`, `ControlState` (`Toggle`/`Button` factories; `Title`, `Icon`, `Symbol`, `IsOn`, `Status`, `Tint`), `IControlService`; `WidgetJson.Serialize(ControlState)` / `DeserializeControl`.
- `WidgetRegistry` scans `[Control]` providers in the same pass as `[Widget]`; new `ControlService`; `UseSpineWidgets` registers `IControlService`. Controls refresh at launch, at backgrounding and in background runs (`RefreshAllInBackground`, `RunBackgroundRefreshAsync`), and after every tap (`HandleControlActionAsync`).
- `IWidgetPlatform`: `AreControlsSupported`, `WriteControl`, `ReloadControl`, `RequestAddControlAsync`; `WidgetIconAssets.EnsureAsync(names)` for tile icons.
- Build: `<SpineControl Include Type DisplayName Description Icon>`; every Apple condition accepts a control where it required a widget (iOS only, not Mac Catalyst); the script validates `Type`, the nine-control cap and kind clashes, writes `controls` into `spine-widgets.json` and `SpineWidgetsControls` into the host Info.plist, and generates `SpineControl_N` structs in an iOS 18 nested bundle (`if #available`) beside the Live Activity, so the main bundle stays within ten.
- iOS native: `SpineControlIntent.swift` (bridge + extension: `SpineControlToggleIntent: SetValueIntent, LiveActivityIntent`, `SpineControlButtonIntent: LiveActivityIntent`, `ControlStore`), `SpineControls.swift` (extension: `ControlValueProvider`, toggle and button configurations), bridge `reloadControls(kind:)`/`reloadAllControls()`. `SpineWidgetIntent.perform` moved into a shared `ActionRelay`; the tap log line carries `"control":true` and `"isOn"`.
- iOS C#: `RecordedAction` has `Control`/`IsOn`; the drain routes control taps to `HandleControlActionAsync` before completing the intent.
- Android: `SpineControlTile` (`TileService`, slots 0–8), manifest services with `BIND_QUICK_SETTINGS_TILE` and `TOGGLEABLE_TILE`, `spine_control_kinds`/`_types`/`_N_label` resources, default `spine_control_icon` drawable, `StatusBarManager.requestAddTileService` on Android 13+.
- Showcase: "Control Center" page (`Pages/ControlCenter/`), `GoalAlertsControl` (toggle) and `GoalControl` (button scoring for the followed club) in `Widgets/Hockey/`, `LiveScore.GoalAlerts` and `GoalForFollowedAsync`, two `<SpineControl>` items, `SampleIndex` entry, `GlobalXmlns`.
- Docs: "Controls" section in `docs/wiki/widgets.md` with two screenshots (`control-center-ios.png`, `quick-settings-android.png`), Limits/Troubleshooting/Testing rows; package README and description; root README rows; `/spine-widgets` §6b and `/spine-setup`.
- Tests: `tests/Plugin.Maui.Spine.Server.Tests/ControlStateJsonTests.cs` (wire names, omitted fields, round trip, non-ASCII text, the document the iOS intent rewrites).

## Decisions

- **Controls live in `Plugin.Maui.Spine.Widgets`, not a new package.** They need the same extension, App Group, bridge and in-process intent; a separate package would duplicate the build pipeline.
- **One interface with both methods** (`GetStateAsync` + `OnActionAsync`) rather than the provider/handler split widgets have: a control without a tap handler is pointless. Async with a context and token, like `IWidgetProvider`, instead of the issue sketch's synchronous `Get()`.
- **`Type` is a required build item attribute.** iOS generates a toggle or a button at build time (the template builder has no if/else), so the type cannot be read from the provider at runtime; a missing or wrong `Type` fails the build instead of guessing.
- **`Icon` means SF Symbol on iOS and SVG on Android, plus an optional `Symbol` override.** iOS controls draw only symbols, so the rasterized-SVG path widgets use is not available. Same naming rule as `W.Icon` otherwise.
- **iOS toggles show the system's own localized On/Off;** `Status` is shown on iOS buttons and on all Android tiles. A custom value label would have been English-only (no string tables in the extension).
- **Toggle intent writes the new value into the stored document before relaying**, so the state holds if iOS ever runs the intent in the extension (the app handles it at next launch, as with widget buttons).
- **Android tiles are not "active" tiles.** Android binds them every time the panel shows them, so the tile draws the stored state and asks the provider for a fresh one each time; no `requestListeningState` bookkeeping.
- **Button tiles are drawn inactive** (the neutral look of Android's own action tiles).
- **Nine controls max**, matching the widget slot count; the iOS controls sit in their own nested bundle so widgets (9) + Live Activity + controls fit the bundle builder's limit of ten.
- **Mac Catalyst gets no controls** (as the issue says); the targets do not pass controls to the Catalyst build, and the Swift is behind `#if !targetEnvironment(macCatalyst)`.
- **`RequestAddAsync` returns `false` on iOS** rather than throwing; there is no API to offer a control.
- **No unlock prompt on Android** (`unlockAndRun` not used), matching iOS controls that run from the Lock Screen.
- **Showcase demo uses the live-score game**: "Goal alerts" (Puckkoll's use case from the issue) and a "Goal Owls" button whose effect is visible across the widget and the Live Activity.
