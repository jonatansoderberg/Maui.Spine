# Issue #387 — Header bar: SmoothEdge, SmoothStatusBar and HardEdge; Auto follows the iOS version

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/387
**Branch:** issue/387-scroll-header
**Status:** Completed
**Related:** #379 (PR #382), #385 (PR #386)

## Plan

Jonatan's iPhone runs iOS 27.0. UIKit's automatic scroll edge style is hard there, while the iOS 26.4 simulator resolves it to soft. That explains why `ScrollEdge` looked hard on the phone in #379.

- Rename `ScrollEdge` to `SmoothEdge`: the soft effect over the whole header, the navigation bar's default on iOS 26.
- Rename `ScrollEdgeHard` to `HardEdge`: the navigation bar's default from iOS 27.
- Add `SmoothStatusBar`: the soft effect behind the status bar only.
- `Auto` follows the navigation bar's default for the running version.

A first attempt, `ScrollHeader`, stretched the soft effect past the bar and was dropped: it made the effect about twice the header's height, which is not what was asked.

## Changes

- `HeaderBarBackground`: `Auto`, `Solid`, `Transparent`, `SmoothEdge`, `SmoothStatusBar`, `HardEdge`. The docs for each value say which version's navigation bar it matches.
- `NavigableMeta.NavigationBarEdge`: `Auto` resolves to `HardEdge` on iOS / Mac Catalyst 27 and later, and to `SmoothEdge` on 26.
- `PagePresenter.Apple.cs`:
  - For `SmoothStatusBar`, the `UIScrollEdgeElementContainerInteraction` goes on a container of its own. That container is as tall as the status bar and holds a label with a space, because UIKit sizes the effect to the labels, images and controls in the container and ignores an empty one.
  - The interaction moves between that container and the title row when the background changes, and both containers' handler changes are watched.
- `PagePresenter.cs`: the Android/Windows stand-in for `SmoothStatusBar` is the soft band behind the status bar only, fading out over 16 points.
- Sample Header bar page: chips Smooth, Status bar and Hard, with the new explanations.
- Docs:
  - `regions.md`: the Backgrounds table and notes, and new six-panel screenshot strips (iOS light and dark, Android).
  - The `/spine-page` skill.

## Verified

- iPhone 17 simulator (iOS 26.4): `Auto` resolves to `SmoothEdge`.
  - With cards scrolled under the bar, `SmoothEdge` blurs the whole header, `SmoothStatusBar` blurs only under the status bar (the row behind the title stays sharp), and `HardEdge` shows the frosted band.
  - Light and dark.
- Pixel 10 Pro emulator: the three stand-ins differ. `SmoothStatusBar` shows a band behind the status bar only.
- iOS 27 was not available in the simulator (Xcode 26.2). The `Auto` → `HardEdge` branch is checked on Jonatan's iPhone.

## Decisions

- **Versions pinned rather than UIKit's automatic style.** Automatic gives different answers on 26.4 and 27.0, and it can differ by context. Pinning makes `Auto` predictable and documented.
- **A container of its own for the status bar.** Resizing the title label would move or cut the title. A second, input-transparent container at the top of the title row changes nothing else, and it is only visible for `SmoothStatusBar`.
- **Renamed without `[Obsolete]`.** These values are still unreleased (not in v0.1.11).
