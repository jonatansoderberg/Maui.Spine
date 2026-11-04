# Issue #781 — Shared element: the source picture never fades out, so a view without a background lands doubled

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/781
**Branch:** issue/781-shared-element-source-fade
**Status:** Completed

## Plan

The flight's picture holds two pictures: the view it leaves as and the view it lands as. Only the second was faded (in); the first stayed at full opacity to the end. For a label or an icon both then show at once, the first scaled up and soft, until the pictures are removed and the view thins on screen. Glukos's value and trend arrow (card → detail page) showed it.

Fade the landing picture in over the first half of the flight and the leaving one out over the second, under a picture that covers it by then: an opaque view never turns see-through, and a transparent one lands as itself. No condition on the view's fill is needed.

## Changes

- `SharedElementFlight.LandingFadeShare` (0.5), shared by both platforms.
- Apple: `FlyAsync` keeps the `from` picture and fades it with keyframes inside the property animator (`to` 0 → 1 over the first half, `from` 1 → 0 over the second), so they take its duration and curve.
- Android: `PictureView.FromAlpha`, set from the eased progress beside `ToAlpha`.
- Docs: `transitions.md`, the shared element's fade.

## Verification

- Glukos on the iPhone 17 Pro simulator with a 0.1.41-dev.1 package, recorded and compared frame by frame (44 → 88 pt value and its arrow): at the frame the pictures are removed the number's ink changes by 0.04 %, where 0.1.39 thinned it by 3.6 %; what is left is anti-aliasing at the glyphs' edges, with no offset.
- Android: built, not run.
