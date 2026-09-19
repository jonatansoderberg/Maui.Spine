# Issue #267 — Back-swipe pan cancels vertical drags in page content on iOS

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/267
**Branch:** issue/267-back-swipe-pan-vertical-drags
**Status:** In Progress

## Plan

`NavigationRegion` puts a MAUI `PanGestureRecognizer` on `_contentHostFront` for the interactive
back-swipe. On iOS and Mac Catalyst that is a `UIPanGestureRecognizer` with `cancelsTouchesInView`,
which recognizes a drag in any direction after about ten points and then cancels the touches of
whatever view is under it — a SkiaSharp canvas handling its own touches loses its finger a few
points into a slow vertical drag. `OnPanUpdated` filters the gesture afterwards, which is too late.

Fix on Apple platforms only: once the content host has its platform view, give the native pan
recognizer a `ShouldBegin` that allows it only for a drag that is rightward, more horizontal than
vertical, started at the leading edge (`_dragAccepted`, from the pointer press) and while the
region can go back. Otherwise the recognizer fails and the touches stay with the content. Android
and Windows are untouched: their pans do not cancel child touches the same way.

Verification: the sample app's back-swipe on a pushed page, and the Almanacka app's page turn
against a locally packed Spine with its workaround removed.

## Changes

- **`NavigationRegion.Apple.cs`** (new) — once `_contentHostFront` has its platform view, the native
  `UIPanGestureRecognizer` gets a `ShouldBegin` that only allows a rightward, mostly horizontal drag
  that started at the leading edge (`_dragAccepted`) while `BackEnabled()`; otherwise the recognizer
  fails and the content keeps its touches. `NavigationRegion` is now `partial` and calls the
  partial method `RestrictBackSwipeOnPlatform()`, a no-op elsewhere.
- **`docs/wiki/regions.md`** — the rule, under "Interactive back-swipe gesture".

## Decisions

- `ShouldBegin` on MAUI's own recognizer rather than a recognizer of Spine's own: MAUI sets its
  delegate through the same lambda properties (`ShouldRecognizeSimultaneously`), so the internal
  delegate takes one more, and the region keeps MAUI's pan events and `OnPanUpdated` unchanged.
- Android and Windows untouched: their pans are checked in `OnPanUpdated` as before, and child
  views get their touches first there.

## Verification

- `samples/MauiSpineSampleApp` on the iPhone 17 Pro simulator: a vertical drag on a pushed page does
  nothing, an edge swipe to the right pops it, no exceptions in the console.
- Almanacka's page turn against a local pack (`0.1.3-local.1`) with its own workaround disabled:
  a slow vertical drag turns the leaf.
