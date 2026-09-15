# Issue #272 — Page lifecycle: OnResumedAsync on ViewModelBase

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/272
**Branch:** issue/272-on-resumed-async
**Status:** In Progress

## Plan

Almanacka subscribes to MAUI `Window.Activated` in a page's code-behind (`Loaded`/`Unloaded`, keeping its own window reference) to refresh what the page shows when the app comes back. Spine owns the window, so Spine should tell the page.

1. **`ViewModelBase.OnResumedAsync()`**: a new `public virtual Task` with an empty default, next to `OnAppearingAsync`, so existing apps compile and behave the same.
2. **`SpineApplication.CreateWindow`** subscribes once to the window's `Deactivated` and `Activated`. An activation that follows a deactivation raises `OnResumedAsync` on the shown pages. The first activation at launch has no deactivation before it, so it raises nothing; those pages have just had `OnAppearingAsync`.
3. **Shown pages** are read from the current host (`SpineHostProvider.Current`, because `SetRootAsync` can swap the tab host and the plain host): the current page of `RootNavigationRegion` (the single root region, or the selected tab's region), and the current page of `ActiveRegionViewModel` when that is the sheet region. Distinct view models only, so a page is never told twice.
4. Docs: the lifecycle hooks in `docs/wiki/regions.md` (the page that documents them), `sheets.md`, `tab-host.md`, a pointer from `page-pattern.md`, and the lifecycle table in `.claude/skills/spine-page/SKILL.md`.
5. Verification on the iOS simulator with a temporary log in the sample: nothing at launch; one `OnResumedAsync` on the shown page after backgrounding (launching Settings) and bringing the sample back with `xcrun simctl launch`.

## Open Questions

None.

## Changes

- `ViewModelBase.OnResumedAsync()`: new `public virtual Task` with an empty default.
- `SpineApplication.CreateWindow` → `HookResumed(window)`: `Deactivated` sets a flag; an `Activated` with the flag set clears it and raises `OnResumedAsync` (fire-and-forget, like the other hooks) on `ShownViewModels()`.
- `ShownViewModels()`: from `SpineHostProvider.Current`, the current page of `RootNavigationRegion` and of `ActiveRegionViewModel` (the sheet region while a sheet is open), distinct.
- Docs: `regions.md` (hook in the example, new *Coming back to the app* section, a note that the calendar day is the app's), `sheets.md`, `tab-host.md`, a pointer from `page-pattern.md`, and the lifecycle table in the `spine-page` skill.

## Decisions

- **No day-change hook.** The first draft of the issue also proposed `OnDayChangedAsync(DateOnly)`, with a `TimeProvider` from DI and a midnight timer. The maintainer decided that a calendar-day hook does not belong in Spine: apps that follow the day can re-check the date in `OnResumedAsync` and run their own timer. So Spine does not read the clock for this feature at all, and registers no `TimeProvider`.

- **Activated after Deactivated, and nothing else.** "First activation at launch" is then simply the activation with no deactivation before it, the same rule on every platform, with no launch-state bookkeeping. The cost is that any loss of focus counts: the notification shade or a system dialog on a phone, and another window on the desktop. The docs say so and tell pages to keep the override cheap. MAUI's `Window.Resumed` was not used, because it is Android's `OnRestart` and iOS's `WillEnterForeground` only, and does not cover desktop focus.
- **Shown is read structurally, not from `ViewModelBase._appeared`.** Sheet pages are never told they disappeared when the sheet closes, so the flag stays set on a closed sheet's view model. The current page of the root (or selected tab's) region, plus the sheet region's while a sheet is open, is what is actually on screen. The page under a sheet counts: it is visible, and it was never told it disappeared.
- **Fire-and-forget**, like the other hooks raised from Spine's own events (`SafeFireAndForget`), so a slow override on one page does not hold up the others.
- **No unit tests.** The logic is a flag and a lookup over `NavigationRegionViewModel`s, which live in the MAUI library. The only test project (`Plugin.Maui.Spine.Server.Tests`, `net10.0`) cannot reference it, and extracting the flag into a MAUI-free class only to test a boolean would be an abstraction for its own sake. Verified on the simulator instead.

## Verification

Temporary, uncommitted instrumentation in the main sample: `Console.WriteLine` in `OnAppearingAsync`/`OnResumedAsync` of `MainPageViewModel`, `SettingsPageViewModel` and `SimpleBottomSheetPageViewModel`. Each resume also moved one step deeper (Main → pushes Settings; Settings → opens the sheet), so that every cycle ran with a different set of shown pages. Stdout was captured with `xcrun simctl launch --console-pty`. Each cycle launched `com.apple.Preferences`, then `xcrun simctl launch`-ed the sample again.

iPhone 17 Pro Max simulator (iOS 26.4), `A7B7DD73-2213-454A-A7B1-B8CB2B082F03`. The sample kept pid 60934 through all cycles (not restarted):

| Step | Shown | Log |
|---|---|---|
| Launch | Main | `OnAppearingAsync(NavigateTo) MainPageViewModel`, and no `OnResumedAsync` |
| Cycle 1 | Main | `OnResumedAsync MainPageViewModel`, once |
| Cycle 2 | Settings (Main covered) | `OnResumedAsync SettingsPageViewModel`, once; nothing on Main |
| Cycle 3 | Sheet over Settings | `OnResumedAsync SettingsPageViewModel` and `OnResumedAsync SimpleBottomSheetPageViewModel`, once each; nothing on Main |

The instrumentation was removed before committing.

- Built: `Plugin.Maui.Spine` for iOS, Android and Mac Catalyst; the main sample for the iOS simulator and for Android.
- **Not run:** Android, because both running emulators (Pixel_10_Pro, Pixel_Tablet) belong to other sessions. Mac Catalyst and Windows were not run either; Windows cannot be built on this Mac.
- Side finding, not changed here: the root page appears with `NavigationDirection.NavigateTo` (`ResetAsync`), while `regions.md` describes `None` as "Page set as root".
