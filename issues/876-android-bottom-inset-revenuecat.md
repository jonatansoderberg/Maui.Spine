# Issue #876 — Android: HidesTabBar pages lose the bottom inset; Plugin.RevenueCat 0.4.44 fails Play purchases

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/876
**Branch:** issue/876-android-bottom-inset-revenuecat
**Status:** Completed

## Plan
- While a page hides the tab bar, give every tab's region the window's own bottom inset (the system navigation: gesture bar, three buttons, a tablet's taskbar) instead of the bar's overlap, and measure the overlap again once the bar is back.
- Bump `Plugin.RevenueCat` from 0.4.44 to 0.4.52.

## Changes
- `SpineTabbedHostPage.Android.cs`: hiding the bar clears each slot's bottom override, so the region falls back to the window's bottom inset; `ApplyTabBarInset` does nothing while the bar is hidden (a layout pass could otherwise put the bar's overlap back), and showing the bar measures it again after the next layout.
- `Directory.Packages.props`: `Plugin.RevenueCat` 0.4.52. The adapter's MAUI 10.0.110 floor is unchanged (0.4.52 needs the same).
- `docs/proposals/spine-purchases.md`: why nothing below 0.4.47.

## Decisions
- The window's inset through `SetBottomOverride(null)`, rather than a margin on the content: the content already reaches the foot of the window for the lightbox, and the page's own safe area is what scrolls its last row clear (`ScrollUnderBottomBar`).
- Checked on the Pixel Tablet emulator with Hockeymatch's settings page on a local build of this branch, its Android-only bottom margin removed: the last row clears the gesture handle and, with three-button navigation, the taskbar; back on the tab bar, the Tabell list ends at the bar with no extra band.
- 0.4.52 rather than the minimum 0.4.47: it is the version a Play license tester bought with in Hockeymatch.
