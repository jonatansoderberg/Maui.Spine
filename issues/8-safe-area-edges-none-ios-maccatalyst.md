# Issue #8 — SafeAreaEdges.None not respected on iOS/Mac Catalyst in sample app main page

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/8
**Branch:** issue/8-safe-area-edges-none-ios-maccatalyst
**Status:** Completed

## Plan

`NavigationRegion` (a `ContentView`) already has the full infrastructure for Spine-managed safe area: `ISystemInsetsProvider.SystemBarInsets` drives both `_frameActionView.Margin.Top` (header position) and `ApplySafeAreaPadding` (per-page content padding). Android works because `SystemInsetsProvider.Android.cs` reports real insets. On iOS and Mac Catalyst, `SystemInsetsProvider` always returned `Thickness.Zero`, so the header had no top margin and content had no padding — yet MAUI's own `ISafeAreaView2` geometry was still applied to `NavigationRegion` (a `ContentView` with default `SafeAreaEdges`), producing the visible gap.

**Root cause:** `NavigationRegion` never opted out of MAUI's automatic safe-area geometry (`SafeAreaEdges.Default` → on iOS, applies system insets as a layout offset). `ISystemInsetsProvider` returned zero on iOS/Mac, so the negative-margin counteraction mechanism (which Android relies on) never fired.

**Approach:**
1. Set `NavigationRegion.SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None` — Spine owns the safe-area contract entirely; MAUI must not apply its own geometry.
2. Remove the `_container.Margin = new Thickness(0, -insets.Top, 0, 0)` assignment from `UpdateContainerMargin` — with `SafeAreaEdges.None`, `NavigationRegion` content fills the full window, so a negative margin would overflow the window.
3. Implement `SystemInsetsProvider` for `IOS || MACCATALYST` — reads `UIWindow.safeAreaInsets` (already in device-independent points), fires `InsetsChanged`.
4. Wire up `UpdateFromUIWindow()` — call it from `HookIosPlatform` (iOS TFM) and `HookMacCatalystPlatform` (Mac Catalyst TFM) on `window.Activated` and on device orientation change.
5. Revert `SpineHostPage.cs` to `this.SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None` (correct .NET 10 API).

## Changes

- Reverted `src/Plugin.Maui.Spine/Presentation/SpineHostPage.cs` — back to `this.SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None` (the broken `SetValue(UseSafeAreaProperty, false)` approach was a dead end).
- Updated `src/Plugin.Maui.Spine/Presentation/NavigationRegion.cs`:
  - Added `this.SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None` in constructor — disables MAUI's ISafeAreaView2 geometry on the content region.
  - Removed `_container.Margin = new Thickness(0, -insets.Top, 0, 0)` from `UpdateContainerMargin` — no longer needed since the region fills the full window without MAUI offsetting it.
- Updated `src/Plugin.Maui.Spine/Core/SystemInsetsProvider.cs` — adds `#if IOS || MACCATALYST` implementation: reads `UIWindow.safeAreaInsets` from the key window via `ConnectedScenes`, fires `InsetsChanged` when values change.
- Updated `src/Plugin.Maui.Spine/Core/SpineApplication.cs` — re-added `partial void HookIosPlatform(Window window)` declaration and call in `CreateWindow`.
- Created `src/Plugin.Maui.Spine/Platforms/iOS/SpineApplication.iOS.cs` — implements `HookIosPlatform`; calls `provider.UpdateFromUIWindow()` on `window.Activated` and on `UIDevice.OrientationDidChangeNotification`.
- Updated `src/Plugin.Maui.Spine/Platforms/MacCatalyst/SpineApplication.MacCatalyst.cs` — added `provider.UpdateFromUIWindow()` call on `window.Activated` at the top of `HookMacCatalystPlatform`, before the tray-icon early return. After reading insets, applies `_host.Padding = new Thickness(0, -top, 0, 0)` to pull content behind the Mac Catalyst title bar (mirrors Windows approach).

## Decisions

- **Spine owns safe area, MAUI does not** — `NavigationRegion.SafeAreaEdges = None` is the authoritative statement: MAUI never applies geometry, Spine always applies it explicitly via `ISystemInsetsProvider` + `ApplySafeAreaPadding`. This mirrors how Android works and makes the platforms consistent.
- **Removed negative-margin pattern** — the negative-margin trick (`-insets.Top` on `_container`) was the Android counteraction mechanism: MAUI offsets NavigationRegion's content by `insets.Top`, so the margin cancels it. With `SafeAreaEdges.None`, MAUI applies no offset, so no cancellation is needed. Keeping the negative margin would have pushed content above the window.
- **No density conversion for UIKit insets** — `UIEdgeInsets` values are already in points (= DIPs on iOS/Mac). No division by screen density needed, unlike Android's pixel-based insets.
- **`HookIosPlatform` is iOS TFM only, Mac Catalyst via `HookMacCatalystPlatform`** — `Platforms/iOS/` compiles for `net10.0-ios` only; Mac Catalyst uses `Platforms/MacCatalyst/`. Both call the same `UpdateFromUIWindow()` method defined under `#if IOS || MACCATALYST` in `SystemInsetsProvider.cs`.
- **Mac Catalyst uses per-page negative `_container.Margin`, iOS uses it unconditionally** — on Mac Catalyst, `fullSizeContentView` makes `UIWindow` cover the title bar, and MAUI's own safe-area offset positions non-full-bleed pages correctly. Only full-bleed pages (`IsHeaderBarVisible=false`, `SafeAreaEdges=None`) apply the negative margin so their content extends behind the title bar. `SystemBarInsets` stays `Thickness.Zero` on Mac Catalyst to avoid double-offsetting `ApplySafeAreaPadding` on pages that use safe area.
- **`NSWindowDidBecomeKeyNotification` used instead of `window.Activated`** — at `window.Activated` time, `UIWindowScene` is still a `_UIPlaceholderWindowScene` and doesn't expose the underlying `NSWindow`. The AppKit notification fires with the real `NSWindow` as its object.
- **`NSWindowStyleMask.FullSizeContentView` (bit 32768) + `setTitlebarAppearsTransparent:`** — these two NSWindow properties are set via P/Invoke to extend UIKit content behind the native title bar. After setting them, `UIView.SetNeedsLayout + LayoutIfNeeded` forces UIKit to update `safeAreaInsets.top` to the title bar height before it is read.
- **`MacTitleBarHeight` on `SystemInsetsProvider`** — Mac Catalyst-specific field separate from `SystemBarInsets`. `SetMacTitleBarHeight` fires `InsetsChanged` so `NavigationRegion.UpdateContainerMargin` re-runs. `NavigationRegion` casts `_insetsProvider` to `SystemInsetsProvider` under `#if MACCATALYST` to read this value.
- **`UpdateContainerMargin` called on `CurrentRegionViewModel` change** — the per-page condition (`IsHeaderBarVisible`, `SafeAreaEdges`) must be re-evaluated on navigation, not only when insets change.
- **`window.Activated` as the trigger** — the safe area is finalised by the time the window is first activated. Subsequent activations are no-ops unless insets changed (guarded by the equality check in `UpdateFromUIWindow`). Orientation changes are handled via `UIDevice.OrientationDidChangeNotification` on iOS.
