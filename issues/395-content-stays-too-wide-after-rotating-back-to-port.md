# Issue #395 — Content stays too wide after rotating back to portrait (iOS)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/395
**Branch:** issue/395-content-stays-too-wide-after-rotating-back-to-port
**Status:** Completed

## Plan

**Reproduced** in the iOS 26.4 simulator (the user rotated by hand): after portrait → landscape → portrait the window is 402 pt wide, but the `Grid` that `NavigationRegion` wraps its pages in is 526 × 894, which is the window plus the landscape safe-area insets (62 left, 62 right, 20 bottom). Every page is laid out at that width, so content runs off the right edge. Rotating the other way happens to come out right.

**Root cause:** `SpineApplication.HookIosPlatform` re-reads `UIWindow.SafeAreaInsets` on `UIDevice.OrientationDidChangeNotification`. That notification says the *device* turned, before UIKit has rotated the interface and settled the window's safe area, so the provider can store the insets of the orientation being left. `NavigationRegion.UpdateContainerMargin` then sets `_container.Margin = -insets` from those stale values, and nothing reads them again until the next rotation.

**Fix:** read the insets when the window's safe area actually changes, not when the device turns.

- In `src/Plugin.Maui.Spine/Platforms/iOS/SpineApplication.iOS.cs`, once the MAUI window has its `UIWindow`, add a small, invisible `UIView` that fills the window and overrides `SafeAreaInsetsDidChange` to call `SystemInsetsProvider.UpdateFromUIWindow(window)`. UIKit calls it after every change to the safe area: rotation (both ways), the status bar appearing or hiding, and the first layout.
- Drop the `OrientationDidChangeNotification` observer, which the observer view replaces. Keep the `Activated` read for the first value.
- Nothing changes in `NavigationRegion`: it already updates the margin and re-applies the pages' safe-area padding on `InsetsChanged`.

**Verify:** the rotation harness in the simulator (portrait → landscape → portrait, several times, both landscape sides, on the start page and on a pushed page); the dump must show the container back at the window's size. Then on the iPhone.

**Apple's guidance** (HIG, Layout): "People expect your experience to remain familiar when they rotate their device"; respect the safe areas so the Dynamic Island and the corners obstruct no content or controls; full-screen backgrounds extend to the edges. Spine's region already fills the window and puts the insets back per page. Two things in landscape did not follow it and were added to this issue at the user's request:

- The header bar's buttons sat at the screen's edge, beside the Dynamic Island: inset them by the left and right safe-area insets, as UIKit's navigation bar does (`NavigationRegion.UpdateContainerMargin`), and keep the page title inside the sides the page leaves unpadded (`PagePresenter.ApplyPageLayout`, from `SafeAreaInsets`).
- The sample's start page is full-bleed (`SafeAreaEdges.None`), so its title and rows ran under the Dynamic Island: the photo stays edge to edge, the title and the list take the side insets.

## Open Questions

## Changes

- `SpineApplication.iOS.cs`: a hidden `SafeAreaObserver` view in the `UIWindow` re-reads the insets on `SafeAreaInsetsDidChange`, replacing the `UIDevice.OrientationDidChangeNotification` observer. Verified in the simulator: portrait → landscape → portrait twice leaves the region's grid at 402 × 970, as before the first rotation (it was 526 × 894); landscape fills the window edge to edge, and a page with the default safe-area edges (Rows) is inset 62 pt left and right.

- `NavigationRegion.UpdateContainerMargin`: the header bar (`_frameActionView`) is inset by the left and right safe-area insets, so its buttons clear the Dynamic Island in landscape.
- `PagePresenter.ApplyPageLayout`: the title row takes the page's `SafeAreaInsets` at the sides (the sides its host does not pad), and re-lays out when `SafeAreaInsets` changes.
- Sample `MainPage`: `TitleMargin` and `ListMargin` from `SystemBarInsets` for the start page's own title (in `HeaderBottomContent`) and the `HeroCollectionView`; the gear's right margin adds the right inset. Verified in the simulator: in landscape the title and the rows clear the Dynamic Island, the cells are 750 wide (inside the safe area) and the photo stays 874 wide; back in portrait everything is at 402 again.

## Decisions

- The start page's rows get a margin on the `HeroCollectionView` rather than `SafeArea.ScrollInset` Left/Right: MAUI's collection view keeps its cells as wide as the view, so a horizontal content inset pushed them 62 pt past the right edge.
- No `HeaderTitleMargin` on `HeroCollectionView`: the sample draws its own title in `HeaderBottomContent` (the built-in title hides then), so the sample's label takes the margin itself.
- An observer view rather than a later re-read on rotation (a delay, or `Window.SizeChanged`): UIKit calls `SafeAreaInsetsDidChange` once the safe area has settled, whatever changed it, so there is no timing to guess.
