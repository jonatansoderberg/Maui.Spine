# Issue #389 — Header bar: rename SmoothEdge and SmoothStatusBar to SoftEdge and SoftStatusBar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/389
**Branch:** issue/389-soft-edge-names
**Status:** Completed
**Related:** #387 (PR #388)

## Plan

Rename `SmoothEdge` to `SoftEdge` and `SmoothStatusBar` to `SoftStatusBar`, at Jonatan's request.

## Changes

- `HeaderBarBackground.SoftEdge` and `HeaderBarBackground.SoftStatusBar`, with every reference to them in `PagePresenter`, `NavigableMeta` and `HeaderBar`.
- Sample Header bar page: the chip is now "Soft".
- `regions.md`, including the relabelled screenshot strips, and the `/spine-page` skill.

## Decisions

- **"Soft"** is UIKit's own name for the style (`UIScrollEdgeEffectStyle.Soft`) and pairs with `HardEdge`.
- **No `[Obsolete]` aliases:** the values have not been released.
