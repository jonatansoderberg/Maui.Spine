# Issue #397 — Header bar: hide the scroll edge at rest, cross-fade the large title, Auto for status bar and foreground, Overlay backgrounds as Normal

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/397
**Branch:** issue/397-header-bar-rest-edge-auto
**Status:** Completed

## Plan

Four findings from testing the header bar on iOS 27:

1. **Scroll edge at rest.** `HardEdge` (Auto on iOS 27) shows its band before anything has scrolled under the bar. Hide UIKit's edge effect (`UIScrollView.TopEdgeEffect.Hidden`) while `ScrollEdgeProgress` is 0, the same signal the Android/Windows stand-ins already fade in with.
2. **Large title cross-fade.** `HeaderBarLargeTitle` takes `1 - HeaderBarCollapseProgress` as its opacity while the page has `LargeTitle`, so it fades out as the bar's title fades in.
3. **Auto naming.** `StatusBarStyle.Default` → `StatusBarStyle.Auto` (not in any released package, so no alias). The sample calls a `null` `HeaderBarForeground` "Auto".
4. **Overlay as Normal.** `Auto` no longer resolves to `Transparent` under `Overlay`; with (1), every background is hidden at rest under both modes and shows once content scrolls. The modes only differ in where the content starts.

## Changes

- `NavigableMeta.ResolveBackground`: `Auto` resolves the same under `Overlay` as under `Normal`.
- `PagePresenter` (Apple): `ApplySystemScrollEdgeRest` hides the top edge effect while the scroll source is at its top; called from `ApplyCollapse` and when the interaction is installed, reset when it is removed.
- `HeaderBarLargeTitle`: opacity follows `1 - HeaderBarCollapseProgress` on a `LargeTitle` page; back to 1 when detached.
- `StatusBarStyle.Default` renamed `Auto` (`SpineOptions`, iOS status bar hint).
- Sample Header bar page: "Auto" for foreground and status bar, Auto/Overlay descriptions.
- Docs: `docs/wiki/regions.md` (backgrounds at rest, Overlay, large title cross-fade, table defaults) and the `/spine-page` skill.

## Decisions

- The edge is hidden for every style, not only `HardEdge`: over an `Overlay` page's photo even the soft edge showed at rest, and the user asked for backgrounds to behave identically in both modes.
- Hidden via `TopEdgeEffect.Hidden` rather than removing the interaction, so the effect's layout (and the iOS 27 soft-edge stretch) stays in place and only appears.
- Verified on the iPhone (iOS 27.0): the edge stays hidden at rest and appears on scroll, the large title cross-fades, Overlay behaves as Normal.
- Apps on Spine that used `Overlay` without a background get the platform's bar on scroll instead of `Transparent`; `HeaderBarBackground = Transparent` keeps the old look.
