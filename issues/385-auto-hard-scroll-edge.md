# Issue #385 — Header bar: Auto should be the hard scroll edge on iOS 26, as native

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/385
**Branch:** issue/385-auto-hard-scroll-edge
**Status:** Completed
**Related:** #379 (PR #382)

## Plan

Jonatan, on an iPhone (iOS 26.5): the native navigation bar's default scroll edge effect is the hard style, so `Auto` should resolve to `ScrollEdgeHard` on iOS 26, not to the soft `ScrollEdge`.

## Changes

- `NavigableMeta.ResolveBackground`: on iOS and Mac Catalyst 26, `Auto` resolves to `ScrollEdgeHard` for a region or tab page whose list fills it from the top. The other branches are unchanged: `Transparent` under Overlay, `Solid` otherwise, and `Solid` on Android, Windows and before iOS 26.
- `HeaderBarBackground` docs: `Auto` names `ScrollEdgeHard` as the native style. `ScrollEdge` is described as softer than a navigation bar's default.
- Sample Header bar page: the explanations of Auto, ScrollEdge and Hard.
- `regions.md`:
  - The Backgrounds table (Auto, ScrollEdge and ScrollEdgeHard rows and their "pick it for" column).
  - The note on styles, and the Large title text.
  - The iOS screenshot strips, light and dark, now labelled "Auto (Hard)".
- The `/spine-page` skill.

## Verified

- iPhone 17 simulator (iOS 26.4), light and dark: the page shows "Auto is ScrollEdgeHard on this page". Scrolled, the Auto bar has the frosted band of the hard style; under the inline title it ends with the items, as `ScrollEdgeHard` did in #379.

## Decisions

- **Hard, not UIKit's automatic style.** Automatic resolved to soft on the 26.4 simulator and looked hard on the 26.5 phone. Pinning hard gives what the native bar shows on the phone, on every 26.x. An explicit `ScrollEdge` stays soft, for an app that wants the lighter look.
