# Issue #508 — Spine.Biometrics: app lock and privacy shield

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/508
**Branch:** issue/508-spine-biometrics-app-lock-and-privacy-shield
**Status:** Completed

Part of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), step 1.3 in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-guards-biometrics.md` §5, together with the owner's decisions of 2026-10-09. It builds on the core guards (#506, PR #526) and on `IUnlock` and `[RequiresUnlock]` (#507, PR #527). When this is merged, phase 1 is done.

## Plan

### Decisions that apply (owner, 2026-10-09 and 2026-10-12)

- The app lock counts from `Window.Stopped`, not `Deactivated`: the Face ID prompt makes an iOS app inactive, and pulling down Control Center should not lock. On the desktop only minimised time counts (Q6), which is what `Stopped` means there.
- The privacy shield is:
  - **Android:** `FLAG_SECURE`, on the activity's window and on every sheet window (a sheet is a `BottomSheetDialog`, a window of its own).
  - **Windows:** `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`.
  - **iOS:** a native `UIVisualEffectView` over the window, added synchronously when the app resigns active, so the app-switcher snapshot shows the cover. iOS cannot block screenshots; the docs say so plainly.
- `Relock = true` on `[RequiresUnlock]` covers the page with the unlock screen after `LockAfter` in the background. The page is not popped, so its state is kept.
- A successful app-lock unlock counts for `[RequiresUnlock]` within the grace period, because the app lock goes through the same `IUnlock`.

### 1. Small public hooks in the core (`Plugin.Maui.Spine`)

The Biometrics package needs three things only the core knows: which pages are on screen, a view above everything (sheets included), and every native window the app has open. Each becomes a small generic hook, with nothing about biometrics in the core (Q1).

- **`SpineOverlay.Show(View content) → IDisposable`** (`Core/SpineOverlay.cs` + `.Apple.cs`, `.Android.cs`, `.Windows.cs`). This is the hook the Highlight proposal asks for (§6.1, approved as Q17), built now without `passTouches`, which Highlight adds.
  - **Apple:** a subview of the key window, as `LightboxOverlay` does, marked `AccessibilityViewIsModal`.
  - **Android:** a full-screen see-through `Dialog`, as `LightboxOverlay` does, since a view in the activity would sit under a sheet's window.
  - **Windows:** a `Popup` on the window's `XamlRoot`, as the sheet does.
  - **A sheet that opens later** (for example from a notification tap while the app is locked) would otherwise cover the overlay. So when a sheet is presented, open overlays are raised again: `BringSubviewToFront` on Apple, and the dialog or popup is reopened on Android and Windows.
  - `LightboxOverlay` stays as it is in this PR.
- **`SpineScreen`** (`Core/SpineScreen.cs`), a static class like `StatusBar`:
  - `ShownPages`: the current page of the root region (the selected tab's) and of the open sheet. This is the list `SpineApplication.ShownViewModels()` builds today, as pages instead of view models.
  - `ShownPagesChanged`: raised when that list changes (a push, a pop, a new root, a tab switch, a sheet opening or closing). `NavigationRegionViewModel` raises it where it already calls `InvokeOnAppearing`, and the hosts raise it on `ActiveRegionChanged`.
  - `WhenReady`: completes on the window's first activation once the first root page is in place (`SpineReadiness`), so a package knows when it can show a prompt.
  - `ExcludeFromCapture` (`bool`):
    - **Android:** `FLAG_SECURE` on the activity window, the open sheet's window and the overlay dialogs. It is applied again when a sheet or overlay opens.
    - **Windows:** `WDA_EXCLUDEFROMCAPTURE` on the window, or `WDA_NONE`.
    - **Apple:** does nothing.

### 2. `Plugin.Maui.Spine.Biometrics`

**Options**
- `SpineBiometricsOptions.AppLock(Action<AppLockOptions>)` turns the app lock on and returns the options, as in the proposal.
- `AppLockOptions`:
  - `LockAfter`: the time in the background before the app locks. The default is `TimeSpan.Zero` (Q3).
  - `LockOnLaunch`: defaults to `true`.
  - `Reason`: text or a Spine string key. The default is the string `Spine.Biometrics.AppLock.Reason`.
  - `IsEnabled`: a `Func<bool>?` for the app's own setting. `null` means always on.
- When `AppLock(...)` is never called, `Relock` pages still use the default `LockAfter`.

**Attributes**
- `PrivacyShieldAttribute` on a page class, with an optional `CoverWhileCaptured`: on iOS the page is covered while the screen is recorded or mirrored (`UIScreen.CapturedDidChangeNotification`).
- `RequiresUnlockAttribute.Relock` (`bool`).

**`AppLock` (internal)** replaces `ForgetWhenStopped` in `SpineBiometricsExtensions`. It is started on the first `SpineScreen.ShownPagesChanged`, when the window exists and its first page is in place, so the cover goes up in the same main-thread turn as the root page and nothing shows first.
- **Launch:** with `LockOnLaunch` and `IsEnabled()`, it shows the lock screen at once and asks once `SpineScreen.WhenReady` completes.
- **`Deactivated`, iOS only:** puts up the native shield when the app lock is enabled or a `[PrivacyShield]` page is shown. Not while Spine's own prompt shows (`UnlockService.IsPrompting`).
- **`Stopped`:**
  - forgets the unlock (as in #507)
  - notes the time
  - On Android before API 30 the device-credential screen is an activity of its own, so the app stops during the prompt. With `LockAfter = 0` it would lock again right after the unlock, so an unlock after the stop cancels it (see Decisions).
- **`Resumed`:** shows the lock screen when the time in the background is at least `LockAfter` and either the app lock is enabled or a shown page has `[RequiresUnlock(Relock = true)]`. The relock uses that page's reason. It does not lock while Spine's prompt is still pending.
- **`Activated`:** takes the shield away. While the lock screen is up and no prompt is showing, it asks at once.
- **Unlocking:**
  - Success removes the lock screen. The unlock goes through `IUnlock.RequireAsync`, so it counts for `[RequiresUnlock]` for `UnlockValidFor`, and a guard waiting at the same time shares the prompt.
  - Cancel leaves the lock screen with its Unlock button.
  - `WhenUnavailable` applies: `Allow` unlocks without asking; with `Deny` the screen stays, with the message.
- **On screen:** on each `ShownPagesChanged`, it sets `SpineScreen.ExcludeFromCapture` when a shown page has `[PrivacyShield]`.
- **Android:** while the app lock is enabled it calls `Activity.SetRecentsScreenshotEnabled(false)` (API 33 and later), which hides the app-switcher thumbnail without blocking screenshots. A `[PrivacyShield]` page blanks the thumbnail through `FLAG_SECURE` on every version.

**Timing logic** sits in a MAUI-free `AppLockClock`, with the stop time, `LockAfter`, `IsEnabled` and the "prompt was showing" rule. Its tests use `FakeTimeProvider`.

**The lock screen** (`LockScreen`, a MAUI view built in code, shown with `SpineOverlay`):
- an opaque page background, so nothing shows through
- the app's name (`AppInfo.Name`)
- the reason
- the Unlock button
- for a `Relock` cover only, a Back button that leaves the page without unlocking (Q2): `BackAsync`, or `CloseAsync` in a sheet; hidden on a root page
- on Android, Back sends the app to the background instead of closing the screen

**Native shield**
- **iOS** (`PrivacyShield.Apple.cs`): a `UIVisualEffectView` (system material) over the `UIWindow`, with the app icon in the middle when one can be read from `CFBundleIcons`.
- **Mac Catalyst:** no snapshot to hide, so no shield.

**`UnlockGuard`:** a guarded root page (`CanCancel = false`) shows the lock screen instead of #507's alert loop. #507 left a note that the full unlock screen would come with this issue.

**Strings:** `Spine.Biometrics.AppLock.Reason` in English and Swedish ("Unlock to continue" / "Lås upp för att fortsätta").

### 3. Tests

- `tests/Plugin.Maui.Spine.Biometrics.Tests` covers `AppLockClock`:
  - `LockAfter`, including zero
  - `IsEnabled` turned off
  - a stop during a prompt being ignored
  - `Relock` without the app lock
  - launch
- The core's guard tests stay green.
- `SpineOverlay` and `SpineScreen` need MAUI, so they are verified in the sample.

### 4. Sample (Spine Showcase, Biometrics page)

- An **App lock** switch, stored in `Preferences`, behind `o.AppLock(a => a.IsEnabled = () => …)` in `MauiProgram`.
- A **Statement** page with `[PrivacyShield, RequiresUnlock("…", Relock = true)]`, opened from the Biometrics page.

### 5. Docs and plan

- `docs/wiki/biometrics.md`:
  - "App lock" and "Privacy shield" sections
  - a platform table that says plainly what iOS cannot do
  - the test recipes
- `SpineOverlay` and `SpineScreen` in the core wiki page where public helpers are listed.
- The package README.
- The proposal's status line.
- `docs/plans/ai-and-security.md`:
  - step 1.3
  - Decisions
  - the Verification backlog (add what was not verified, remove what was)
  - Last updated
  - phase 1 done

### Verification

- **Builds:** the core and Biometrics for iOS, Android and Mac Catalyst, and Windows compiled on the Mac.
- **iPhone 17 simulator:**
  - the app-switcher snapshot shows the shield (app lock on, and a `[PrivacyShield]` page)
  - lock on launch
  - relock after `LockAfter`, keeping the page
  - one prompt covering both the app lock and a guard
  - `CoverWhileCaptured`, if the simulator can record
- **Android emulator, with a temporary PIN:**
  - `FLAG_SECURE` on the activity and on a sheet (`dumpsys window`, `screencap` is black)
  - the lock screen over an open sheet
  - Back on the lock screen
  - the PIN removed afterwards
- **Mac Catalyst on this Mac:** whether minimising raises `Stopped`, which is the desktop half of Q6.
- **Not possible here:** Windows at run time, so it goes to the Verification backlog.

## Open Questions

None. The owner answered Q1–Q3 on 2026-10-12; see Decisions.

## Changes

- **Core (`Plugin.Maui.Spine`):**
  - `SpineScreen` (`Core/SpineScreen.cs`, Android and Windows parts):
    - `ShownPages` and `ShownPagesChanged`. It is raised from `NavigationRegionViewModel` (`CurrentRegionViewModel` and `BackView`), from both hosts' `ActiveRegionChanged` and from `SwapHost`, and only when the list changed.
    - `WhenReady`
    - `ExcludeFromCapture`:
      - **Android:** `FLAG_SECURE` on the activity, the sheet's dialog (`BottomSheetPageExtensions.ActiveSheetWindow`), the lightbox's dialog and overlays. A window opened later gets it too.
      - **Windows:** `SetWindowDisplayAffinity`.
      - **Both:** only what Spine set is cleared.
  - `SpineOverlay.Show(View, Action? back)`:
    - **Apple:** a view on the window.
    - **Android:** a dialog of its own.
    - **Windows:** a popup.
    - **A sheet that opens while it shows:**
      - **Apple:** raised again in the sheet controller's `ViewWillAppear`, and also when the window first becomes visible.
      - **Windows:** raised again after the sheet's popup opens.
      - **Android:** the sheet's window is held invisible under the overlay until the last overlay goes (`HoldUnder`).
    - The content is a logical child of the window's page, so the app's styles apply.
- **Biometrics:**
  - `SpineBiometricsOptions.AppLock(...)` and `AppLockOptions`: `LockAfter`, `LockOnLaunch`, `Reason` and `IsEnabled`.
  - `PrivacyShieldAttribute` with `CoverWhileCaptured`, and `RequiresUnlockAttribute.Relock`.
  - `AppLockClock`, the MAUI-free timing.
  - `AppLockService`, an `IMauiInitializeService`. It replaces `ForgetWhenStopped` and covers:
    - launch, `Deactivated`, `Stopped`, `Resumed` and `Activated`
    - the cover and the prompt
    - Back on a relocked page
    - `HoldAsync` for a guarded root page
  - Platform parts:
    - **iOS:** the native shield and the capture cover.
    - **Android:** `SetRecentsScreenshotEnabled`.
    - **Mac Catalyst and Windows:** a watcher for minimise.
  - `LockScreen`: the app's name, the reason, Unlock and the optional Back button.
  - `UnlockService.IsPrompting`.
  - `UnlockGuard` uses `HoldAsync` instead of #507's alert loop.
  - Strings `Spine.Biometrics.Back` and `Spine.Biometrics.AppLock.Reason`, in English and Swedish.
- **Tests:** `AppLockClockTests`, 9 tests. Biometrics 19/19 and core 99/99 pass.
- **Sample:**
  - an "App lock" switch on the Biometrics page, read by `IsEnabled` in `MauiProgram`
  - a "Privacy shield" example
  - a new `StatementPage` with `[PrivacyShield(CoverWhileCaptured = true), RequiresUnlock(…, Relock = true)]`
- **Docs:**
  - `docs/wiki/biometrics.md`: app lock, relock, the privacy shield table and testing
  - `docs/wiki/guards.md`: "The whole screen", with `SpineScreen` and `SpineOverlay`
  - the package README
  - `README.md` and `docs/wiki/packages.md`
  - the `spine-setup` and `spine-page` skills
  - the proposal's status line
  - the plan

## Verified

- **Builds:** the core and Biometrics build for iOS, Android and Mac Catalyst, and for Windows on the Mac. The sample builds for the iOS simulator, Android and Mac Catalyst.
- **iPhone 17 simulator (iOS 26.4, Face ID enrolled):**
  - **Statement page:**
    - The app switcher shows the blurred cover with the app icon instead of the account.
    - Back in the app, the lock screen with a Back button covers the page and Face ID asks at once.
    - A match uncovers the page, which is still there.
    - A failed match and Cancel leave the lock screen without asking again.
    - Back pops the page and takes the cover away without unlocking, and `ExcludeFromCapture` goes off.
  - **App lock on:**
    - A cold start shows the lock screen, and Face ID asks.
    - A recording of the cold start has no frame of the start page before the lock screen.
    - A guarded page opened while the app lock asks shares its prompt: one match opened both.
    - With the app lock on, a guard's Face ID prompt shows no shield behind it.
    - The app switcher shows the cover over the start page, and the return locks.
    - The lock screen covers an open sheet.
    - A sheet opened while locked stays under the lock screen. A recording has no frame of it.
  - **Guarded root page** (`SetRootAsync` to `PinPage`): Cancel shows the lock screen without Back, and Unlock and a match make it the root.
- **Android emulator (Pixel 10 Pro, API 37):**
  - **Statement page:**
    - `screencap` is black.
    - `FLAG_SECURE` is on the activity, and also on a sheet opened over the page, where only the system status bar shows in a screenshot.
    - It is cleared after leaving.
  - **With a temporary PIN and the app lock on:**
    - A cold start shows the lock screen and `BiometricPrompt`. The PIN unlocks, and the app does not lock again.
    - The lock screen's dialog is above the sheet's window.
    - A sheet opened while locked stays out of sight. A `screenrecord` has no frame that differs from the lock screen. After the PIN, the sheet shows.
    - Back on the lock screen sends the app to the background.
    - The overview's card, opened from the home screen, is blank. With the app lock off, the same card shows the app.
  - The PIN and the preference were removed afterwards.
- **Mac Catalyst (this Mac, Touch ID):**
  - Minimising the window puts its scene in the background, but MAUI raises no window events.
  - With the scene watcher, the clock's stop time is set on minimise and cleared on restore.
  - Another scene's background notification at launch is ignored.
- **Not verified, now in the plan's Verification backlog:**
  - Windows at run time
  - the full lock and Touch ID after a minimise on the Mac
  - `CoverWhileCaptured` on an iPhone
  - Android before API 30
  - a relocked page in a sheet

## Decisions

- **SpineScreen.ShownPages lists the page behind during a transition and under a lightbox,** so `FLAG_SECURE` stays on until a shielded page has left the screen. For the same reason `SpineApplication.ShownViewModels()` was left as it was, unlike the plan said: reading from `ShownPages` would also send `OnResumed` to the page under a lightbox.
- **The app lock starts on the first `ShownPagesChanged` and takes the window from the page.** On a cold start the first page goes in inside `CreateWindow`, before `Application.Windows` lists the window, and the next identical update is deduplicated, so a window lookup there missed the launch lock in the first run. The cover waits for the window's handler.
- **The overlay on Apple is raised in the sheet's `ViewWillAppear` and when the window becomes visible.**
  - **The sheet:** a recording showed the sheet sliding over the lock screen for half a second when it was raised right after `PresentViewController`, because the presentation's container goes into the window only when the transition starts.
  - **A cold start:** the root view went in above the overlay at `MakeKeyAndVisible`.
- **Leaving for the prompt.**
  - A later unlock cancels the stop, as with Android's device-credential activity.
  - `Resumed` does not lock while Spine's prompt is pending.
  - A stop is not ignored while prompting. Otherwise pressing Home during Face ID would skip the lock.
- **The iOS shield is skipped while Spine's own prompt shows,** since the Face ID sheet makes the app inactive. It is also skipped while the lock screen is up, which already hides the app.
- **Mac Catalyst minimise is watched on the scene.** MAUI follows the application's lifecycle there, and the application stays active while the window is minimised. The notification is matched to the window's scene when it comes, because the window has no scene yet on a cold start and another scene goes to the background at launch.
- **Windows minimise and tray are watched on `AppWindow.Changed`.** This was not run, and MAUI may report it too. A second report does nothing, because the clock keeps the first stop and `Lock()` and the prompt are idempotent.
- **Android recents:** `SetRecentsScreenshotEnabled(false)` is only set by Spine while the app lock is on, and only undone if Spine set it.
- **The overlay's content is a logical child of the window's page** (`AddLogicalChild`), so the lock screen's Unlock button gets the app's button style.
- **On Android a sheet that opens under an overlay is held invisible, not covered again.** The first try dismissed and showed the overlay's dialog again to get it back on top. A `screenrecord` showed the app's page for two frames in between, so a window shown later is now kept invisible until the overlays have gone.
- **`SpineOverlay.Show` takes a `back` action** for Android's back button. The lock screen uses it to send the app to the background, and a relocked page uses it to leave the page.

- **2026-10-12, owner (Q1):** the core gets small public hooks, `SpineOverlay` and `SpineScreen`, rather than `InternalsVisibleTo` for the package.
- **2026-10-12, owner (Q2):** a `Relock` cover (not the app lock) has a Back button that leaves the page without unlocking. It calls `BackAsync`, or `CloseAsync` when the page is in a sheet. The button is hidden when the page is a root page.
- **2026-10-12, owner (Q3):** `LockAfter` defaults to `TimeSpan.Zero`, so the app locks on every return from the background.
