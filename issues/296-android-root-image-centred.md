# Issue #296 — Android: an image at a widget's root is drawn at the start edge, not centred

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/296
**Branch:** issue/296-android-root-image-centred
**Status:** Completed

## Plan

Almanacka's store preflight (D1) found the leaf picture of its small widget left of centre on Android. The picture is the whole tree, `W.Image(asset).FullColor()`. The preflight blamed the `WidgetSurface` picture, but the screenshot shows the plain widget, which has only a gradient, off by the same amount: 47 px of a 390 px tile in both. So the surface is not the cause.

`spine_root` in `spine_widget_root.xml` is a vertical `LinearLayout` with `android:gravity="center_vertical"`. It centres the tree vertically and leaves it at the start edge horizontally. Stacks are `match_parent` wide, so a tree that starts with a stack never shows this. A root text, timer, icon or image is `wrap_content` and sits at the start edge. SwiftUI centres a root view on both axes, so iOS draws these centred.

1. `spine_root` gets `android:gravity="center"`. A root stack is unaffected, since it fills the width. Any root node narrower than the widget is centred, as on iOS.
2. Reproduce and verify in `samples/MauiSpineSampleApp`: add a second 2×2 widget, `card`, whose tree is `W.Image(...).FullColor()` on a gradient with a picture over it (a `WidgetSurface`). This is Almanacka's case. Its PNGs go in `Resources/Raw` and are stored from the provider.
3. Verify on emulator-5556 (Pixel_10_Pro, Android 17) and on the Pixel_Tablet AVD (Android 16). There is no Android 12 AVD or system image on this machine. The layout change does not depend on the API level, so no separate test was run there.
4. docs/wiki/widgets.md: note under *Android → How it maps* that a root node narrower than the widget is centred.

## Open Questions

## Changes

- `spine_widget_root.xml`: `spine_root` has `android:gravity="center"` (was `center_vertical`).
- Sample: `CardWidget` (`card`, Small) in `samples/MauiSpineSampleApp`. Its tree is `W.Image("sample_card.png", height: 120).FullColor()` on a `WidgetSurface` of a gradient and `sample_backdrop.png`. The PNGs are in `Resources/Raw`: a 240×300 card drawn for the purpose, and the push sample's picture.
- docs/wiki/widgets.md: *Images* says a root node narrower than the widget is centred and names the sample; the *How it maps* row for stacks says so for Android; *Related* lists the new sample file.
- Reproduced on emulator-5556 (Pixel_10_Pro, Android 17) before the fix. The card's centre was at x≈250 px in a tile centred at x≈339 px. After the fix, on the Pixel_Tablet AVD (Android 16), the card and the tile are both centred at x≈1280 px. emulator-5556 was in use by another session at that point; see Decisions.

## Decisions

- The preflight blamed the surface picture, but it is not the cause. The plain widget, which has only a gradient, is off by the same amount in the same screenshot. The fix is in the root frame, not in the surface views.
- `gravity="center"` on the root and no change to the image stub's `fitStart`. With `adjustViewBounds` and `wrap_content` the image view is exactly the size of its bitmap, so the scale type does not move it. Only the parent's gravity places it.
- No Android 12 test. There is no Android 12 AVD or system image on this machine. `LinearLayout` gravity behaves the same on every API level, so the only version-dependent part here, the clip to the rounded outline, is unchanged.
