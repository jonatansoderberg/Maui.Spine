# Issue #507 — Spine.Biometrics: IBiometricAuth, [RequiresUnlock] on pages and commands

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/507
**Branch:** issue/507-spine-biometrics-ibiometricauth-requiresunlock-on
**Status:** Completed

Part of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), step 1.2 in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-guards-biometrics.md` §3–§4, together with the owner's decisions of 2026-10-09. It builds on the core guards from #506 (PR #526).

## Plan

A new package, `src/Plugin.Maui.Spine.Biometrics`. It depends on `Plugin.Maui.Spine` and `Plugin.Maui.Spine.Common`, and on Android it adds `Xamarin.AndroidX.Biometric`.

1. **`IBiometricAuth`**
   - `GetAvailabilityAsync()` returns `BiometricAvailability(Kind, Enrolled, HasDeviceCredential)`.
   - `AuthenticateAsync(reason, BiometricOptions?, CancellationToken)` returns a `BiometricResult`.
   - Platforms:
     - **iOS and Mac Catalyst:** `LAContext`, a new one per evaluation, with the `DeviceOwnerAuthentication` policy by default.
     - **Android:** `BiometricPrompt` with `BIOMETRIC_WEAK | DEVICE_CREDENTIAL` on the current `FragmentActivity`.
     - **Windows:** `UserConsentVerifier` and `UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd)`.
2. **`IUnlock`**
   - `IsUnlocked`, `RequireAsync(reason, fresh)`, `Lock()` and `LockChanged`.
   - One unlock per app, valid for `UnlockValidFor` (1 minute, owner decision 2).
   - `fresh` always asks.
   - Calls made at the same time share one prompt.
   - The unlock is cleared when the window stops, which is the background on mobile and minimised on the desktop.
   - With no device lock, `WhenUnavailable` decides; the default is `Allow` (decision 3).
   - A lock-out shows an alert.
   - The logic is free of MAUI types so it can be unit tested with `FakeTimeProvider`.
3. **`[RequiresUnlock("reason", Fresh = …)]`**
   - A `GuardAttribute` with `Order = 200`, answered by `UnlockGuard`.
   - Pages and commands get it through the core hooks.
   - `UnlockGuard` is also the `IGuardCover` for guarded tabs on Android and Windows: a "Locked" view with an Unlock button.
   - When the guard cannot cancel (root page), it keeps asking behind a one-button alert until the user unlocks.
4. **`Unlock.Command`, `Unlock.Reason` and `Unlock.Fresh` attached properties**
   - On a `Button` they set `Button.Command` to a wrapper that asks first.
   - On any other view they set `Tap.Command` the same way.
5. **`UseSpineBiometrics(o => …)`**
   - Registered as a `<SpineModule>`, idempotent, following the `UseSpineBackgroundTasks` pattern.
   - Options: `UnlockValidFor` and `WhenUnavailable`. The app lock is #508.
6. **Face ID usage text** (decision 4): `build/Plugin.Maui.Spine.Biometrics.targets` fails an iOS build whose Info.plist lacks `NSFaceIDUsageDescription`, with a clear message. The package writes no default text. At run time a missing key gives `NotAvailable` and a log line instead of the system terminating the app.
7. **Strings:** `Spine.Biometrics.*` in English and Swedish (embedded XML, as in Shimmer).
8. **Tests:** `tests/Plugin.Maui.Spine.Biometrics.Tests` covering:
   - the grace period
   - `fresh`
   - coalescing
   - `WhenUnavailable`
   - lock-out
   - `Lock()`
9. **Sample:** a Biometrics page in Spine Showcase:
   - the device's availability
   - an unlock button using `RequireAsync`
   - a guarded page
   - a guarded command from a menu row
   - `Unlock.Command` on a plain button

   The sample gets `NSFaceIDUsageDescription` and imports the package targets.
10. **Docs and package lists:**
    - a new `docs/wiki/biometrics.md`
    - a package README
    - `README.md` and `docs/wiki/packages.md` (19 packages)
    - `.claude/skills/spine-setup`
    - the package icon `assets/icons/biometrics.png`
    - the plan: step 1.1 Done, step 1.2 In review

## Open Questions

None. The owner's decisions of 2026-10-09 cover the choices.

## Changes

- **New package `Plugin.Maui.Spine.Biometrics`:**
  - Code:
    - `IBiometricAuth` with Apple, Android and Windows implementations
    - `IUnlock` and `UnlockService`
    - `RequiresUnlockAttribute`, `UnlockGuard` (guard, tab cover, and a root page that keeps asking)
    - the `Unlock` attached properties
    - `UseSpineBiometrics`, with `SpineBiometricsOptions` and `UnavailablePolicy`
    - English and Swedish strings
  - Build files:
    - `<SpineModule>` in `build/*.props`
    - the Face ID usage check in `build/*.targets`
    - `Xamarin.AndroidX.Biometric` 1.1.0.30 in `Directory.Packages.props`
- **Tests:** `tests/Plugin.Maui.Spine.Biometrics.Tests` has 10 tests covering the grace period, `fresh`, a shared prompt, cancel, lock-out, both `WhenUnavailable` policies, a missing usage key, `Lock()` and the device credential. The core's 99 tests still pass.
- **Sample:** a Biometrics page and a PIN page in Spine Showcase, `NSFaceIDUsageDescription` in the iOS and Mac Catalyst Info.plist, the package targets imported, and a row on the main list.
- **Docs:**
  - `docs/wiki/biometrics.md`, linked from getting-started and from the guards page
  - the package README
  - `README.md` and `docs/wiki/packages.md` (nineteen packages, the graph, tables, build assets)
  - `.claude/skills/spine-setup`
- **Icon:** `assets/icons/biometrics.png`, with its source in `assets/logo-src`.
- **Solutions:** `Spine.slnx` and `Spine.Packages.slnf`.

## Verified

- **Builds:**
  - The package builds for iOS, Android, Mac Catalyst and Windows (Windows compiled on the Mac).
  - The NuGet package packs with its icon, README, `build`/`buildTransitive` files and three TFMs. The Windows TFM is packed on Windows.
  - Without `NSFaceIDUsageDescription`, the sample's iOS build stops with the Spine message.
- **Spine Showcase, iPhone 17 simulator with Face ID enrolled:**
  - Availability shows `FaceId, enrolled`.
  - The guarded PIN page opens after a matching face.
  - `Unlock.Command` runs without asking inside the minute.
  - The `Fresh` menu row asks again and runs after a match.
  - After the app goes to the background and returns, the page asks again.
  - A non-matching face followed by Cancel refuses with no message.
- **Android emulator, in the scratch tabbed app from #506 with `[RequiresUnlock]` on a tab:**
  - **Without a screen lock:** `Biometric enrolled=False credential=False`, and `Allow` lets it through without a prompt.
  - **With a temporary PIN:**
    - `BiometricPrompt` shows with the reason.
    - Entering the PIN opens the tab.
    - Back cancels and leaves the "Locked" cover with its Unlock button.
  - The PIN was removed again afterwards.
- **Not verified:**
  - Windows Hello at run time.
  - Touch ID on a Mac.
  - Fingerprint on Android (no enrolled finger on the shared emulator).
  - The lock-out message.
  - A guarded root page.

## Decisions

- **`Xamarin.AndroidX.Biometric` 1.1.0.30, not 1.1.0.33.** 1.1.0.30 is the last build on MAUI 10.0.50's AndroidX releases (Activity 1.10.1, Fragment 1.8.8, Lifecycle 2.9.2). 1.1.0.33 lifts them, the Ktx facades MAUI pins fall out of range (18 NU1608 warnings), and the result is the binding drift seen in #451.
- **An unavailable device does not count as an unlock.** With `WhenUnavailable = Allow` every guard passes on a device without a lock, but `IsUnlocked` stays false, so the app can still tell.
- **The unlock is forgotten on `Window.Stopped`.** A window is watched from the first successful unlock, since there is nothing to forget before that.
- **A guarded root page keeps asking** behind a one-button alert. There is no page to fall back to, and the full unlock screen of the app lock comes with #508.
- **The Showcase sample uses a menu row** for the `Fresh` command, because the header bar's single trailing slot holds the theme menu on sample pages.
