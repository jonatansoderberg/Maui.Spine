# Issue #269 — Keyboard avoidance: MAUI's SoftInput safe-area edge does nothing inside a Spine page

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/269
**Branch:** issue/269-keyboard-avoidance-mauis-softinput-safe-area-edge
**Status:** Completed

## Plan

Keyboard avoidance is part of Spine's safe-area contract and **on by default** for regions, tabs and sheets. A page can opt out.

### Why MAUI's `SoftInput` does nothing
Every Spine host sets MAUI's `SafeAreaEdges = None` (`NavigationRegion.cs:62`, `SpineHostPage.cs:67`, `SpineTabPage.cs:16`). On iOS `NavigationRegion` also cancels the window inset with a negative container margin, and it pads each content host itself in `ApplySafeAreaPadding`. MAUI's keyboard inset travels the same safe-area path, so nothing reacts to it. On Android, `SystemInsetsProvider.OnApplyWindowInsets` reads `Type.SystemBars()` only, and the activity's soft-input mode is never set. The only keyboard handling today is in the Android sheet wrapper (`BottomPaddingInsetsListener`).

### Mechanism
1. **The keyboard's overlap with the region.** `NavigationRegion` computes how far the keyboard covers the region's own frame in window coordinates: `max(0, regionBottom - keyboardTop)`. That is the same number for a region, a tab (the tab bar sits under the keyboard) and a sheet (UIKit and Material move the sheet themselves, and only the rest is covered).
   - **iOS / Mac Catalyst:** `UIKeyboard.Notifications.ObserveWillChangeFrame` with the end frame, duration and curve, in a small `KeyboardObserver`.
   - **Android:** set the activity window to `SoftInput.AdjustResize` (with edge-to-edge it does not resize, it only dispatches IME insets), read `WindowInsetsCompat.Type.Ime()` in a listener on the region, and follow `WindowInsetsAnimationCompat` so the padding moves with the keyboard frame by frame. The sheet keeps its dialog `AdjustResize` and its wrapper listener.
   - **Windows:** `InputPane` `Showing`/`Hiding` if it is cheap; otherwise no-op (no on-screen keyboard on most Windows devices).
2. **Applying it.** `ApplySafeAreaPadding` sets the content host's bottom padding to `max(bottomInset, overlap)` instead of `bottomInset` while the keyboard is up. Content and footer both end above the keyboard, as with `adjustResize` and UIKit's keyboard layout guide. The change is animated on iOS with the keyboard's duration.
3. **Exposing it.** `ViewModelBase.KeyboardInset` (`double`, the overlap in device-independent units) next to `SafeAreaInsets`, so a page or the planned header search (`docs/proposals/spine-header-search.md` §7) can read it. It is set for the current page and reset to 0 on hide and on navigation.
4. **Opting out.** `KeyboardAvoidance` (`bool`, default `true`) on `NavigableRegionAttribute` / `NavigableTabAttribute` / `NavigableSheetAttribute`, inherited from `SpineOptions.RegionDefaults` like `SafeAreaEdges`, and mirrored on `ViewModelBase`. With it off the padding is left alone, but `KeyboardInset` is still reported.
5. **MAUI's own iOS `KeyboardAutoManagerScroll`.** It may shift the view on top of Spine's padding. Measure in the simulator. If it double-shifts, Spine disconnects it while its own avoidance is active.
6. **MAUI's `SafeAreaEdges="SoftInput"` inside a page** is documented as not supported under Spine, with `KeyboardAvoidance` / `KeyboardInset` named as the replacement.

### Files
- `Presentation/NavigationRegion.cs` — the overlap, `ApplySafeAreaPadding`, `KeyboardInset` on the current page.
- New `Platforms/iOS` + `MacCatalyst` keyboard observer, and an Android IME listener and animation callback (near `SystemInsetsProvider.Android.cs`).
- `Core/NavigableAttribute.cs`, `Services/NavigableMeta.cs`, `SpineOptions` region defaults, `Core/ViewModelBase.cs`.
- Android activity soft-input mode in `SpineApplication.Android.cs`.
- Docs: `docs/wiki/regions.md` (safe areas), `docs/wiki/sheets.md`, `docs/wiki/tab-host.md`.
- Showcase: a sample page with an `Entry` at the foot of a grid, in a region, a tab and a sheet (the login sheet already has an email field).

### Verification
iPhone 17 Pro simulator with the hardware keyboard disconnected, and the Pixel emulator. For each of region, tab and sheet: the bottom entry stays above the keyboard, the padding goes away on hide, and the footer stays above the keyboard. With `KeyboardAvoidance = false` nothing moves.

## Open Questions

- Windows: implement `InputPane` now or leave it at no-op? Decided during implementation by cost.

## Changes

- `KeyboardAvoidance` on `NavigableAttribute` (all three kinds) and `NavigableDefaults`, default `true`; mirrored on `ViewModelBase` with `KeyboardInset`.
- `Core/SoftKeyboard`: where the keyboard's top edge is on screen, reported by the platform. iOS: `UIKeyboard` will-change-frame (`Platforms/iOS/SoftKeyboard.iOS.cs`), with duration and curve; a keyboard that does not reach the screen's bottom (floating, undocked) counts as down.
- Android: `SystemInsetsProvider` reads `Type.Ime()`, consumes it, and follows the keyboard frame by frame through a `WindowInsetsAnimationCompat` callback on the window's root (activity and sheet dialog). The activity window is set to `adjustResize`.
- `NavigationRegion`: the overlap with the keyboard (only in the region that holds the focus) becomes the page's `KeyboardInset` and the content host's bottom padding (`max(bottom inset, overlap)`). iOS lays out inside a UIKit animation with the keyboard's duration and curve. `SafeAreaInsets.Bottom` is 0 while the keyboard is up. Recomputed when the region resizes, and when `KeyboardAvoidance` changes live.
- Showcase: "Keyboard" page (chat layout, footer composer, live switch, `KeyboardInset` readout, the same page as a sheet).
- Docs: `regions.md` "The on-screen keyboard", attribute tables in `regions.md` and `sheets.md`, a line in `tab-host.md`.

## Decisions

- **On by default with opt-out** (Jonatan, 2026-10-05): pages should need nothing; a page that handles the keyboard itself sets `KeyboardAvoidance = false`.
- **Only the region that holds the focus moves.** iOS walks the region's views for the first responder; Android asks `FindFocus()`. Otherwise the page under a sheet (or another tab) relaid itself behind it.
- **An Android sheet is measured against the screen's bottom, not its own frame.** Material keeps the sheet full height, slides it per detent and expands it while the keyboard comes up, and the wrapper drops its navigation-bar padding then; measuring the frame mid-way gave 0, 312 or 514 instead of 336.
- **The Android animation callback sits on the window's root** with `DispatchModeContinueOnSubtree`. MAUI 10's `MauiWindowInsetListener` registers its own callback lower down with `DispatchModeStop`, so a callback on Spine's host view never fired; on the root it runs first and MAUI's still runs.
- **On Android only the window with the focus reports the keyboard.** The activity window gets the keyboard's end-state insets before a sheet's dialog starts its animation, which made the sheet jump to the end, back to 0 and animate up again. The sheet's wrapper listener reports for the dialog's window.
- **Gboard on the emulator switches to its floating physical-keyboard toolbar after `adb shell input keyevent`.** That toolbar reports no IME inset (correctly nothing moves), but it hides the soft keyboard until the emulator restarts; test by tapping only.
- **MAUI's iOS `KeyboardAutoManagerScroll` left alone.** In the simulator it did not move anything on top of Spine's padding (with avoidance off, the field simply stayed covered), so there was nothing to disconnect.
- **Windows: no on-screen keyboard handling** (`SoftKeyboard.Top` stays null). Most Windows devices have no touch keyboard; `InputPane` can be added later.
- **The whole content host is lifted, footer included**, rather than adding the overlap to the scroll inset. That matches `adjustResize` and UIKit's keyboard layout guide, and keeps a footer button such as Log in reachable.
