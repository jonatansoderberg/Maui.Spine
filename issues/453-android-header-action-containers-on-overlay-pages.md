# Issue #453 — Android: header action containers on Overlay pages beyond HeaderBarGlass.Clear

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/453
**Branch:** issue/453-android-header-action-containers-on-overlay-pages
**Status:** Completed

## Plan

Where header actions are not glass (Android, Windows, iOS before 26), an icon action on an `Overlay` page gets a container that keeps it readable over whatever lies under the bar. The container fades out while the bar's own background fades in.

### Today
`PageActionView.Face.Scrim` (`PageActionView.cs:524`) is true only for `HeaderBarGlass.Clear`. It draws a 40 dp black circle at 40 % (`ScrimFill`) through the "filled" path (`Filled`, `ApplyShape`, `ApplyForeground`, `ApplyFilledStates`). `Regular` on an `Overlay` page gets no container. Nothing ties the circle to the bar's background, which `PagePresenter.ApplyCollapse` fades in with `ViewModelBase.ScrollEdgeProgress`.

### Changes
1. **What the action lies over.** `HeaderBarView` gets two internal properties, passed on to its `PageActionView`s:
   - `OverContent`: the header page's `HeaderBarMode == Overlay`.
   - `BackgroundProgress`: how far the bar's background has faded in. This is `ScrollEdgeProgress` when the page has a background that is drawn (anything but `Transparent`), else 0.

   `NavigationRegion` sets both from the header view model (`HeaderRegionViewModel`), next to the bindings it already sets there. It sets them from `PropertyChanged` because `ScrollEdgeProgress` is internal and cannot be bound.
2. **The tonal container.** In `Face`, a new `Tonal` case: not glass, not prominent, `Glass == Regular` and `OverContent`.
   - It is Material 3's filled tonal icon button: a 40 dp circle in a surface-container tone, slightly translucent, with the icon in on-surface.
   - Light theme: `#F3EDF7`-like neutral at about 90 %. Dark theme: `#2B2930`-like at about 90 %. The icon keeps the theme's foreground.
   - With a fixed `HeaderBarForeground`, the circle takes the tone that contrasts with that colour (light icon → dark circle), so a white icon over a photo does not land on a light circle.
   - `Filled` becomes `_prominent || Scrim || Tonal`, so shape, padding and pressed/disabled states come from the existing filled path.
3. **Fading with the background.** For `Scrim` and `Tonal`, the button's fill alpha is scaled by `1 - BackgroundProgress`. At rest the circle shows; once the bar's background is in, the action is a plain Material icon button again. Only the fill fades, so the glyph and touch target stay.
4. **Normal pages are unchanged:** no container, Material's standard icon button. `Clear` keeps its dark scrim on any page, and now fades as well.
5. **Windows** takes the same path (no glass). It is checked by reading the code only, since there is no Windows machine.

### Files
- `src/Plugin.Maui.Spine/Presentation/PageActionView.cs`: `Tonal`, the tonal fill, fading.
- `src/Plugin.Maui.Spine/Presentation/HeaderBarView.cs`: `OverContent`, `BackgroundProgress`, passed to both action views.
- `src/Plugin.Maui.Spine/Presentation/NavigationRegion.cs`: watch the header page.
- `docs/wiki/regions.md`: the `HeaderBarGlass` row and the Overlay header section.

### Verification
Pixel 10 Pro emulator, Showcase "Header bar" sample, Overlay with Regular and Clear, light and dark:
- the circle shows at rest over the hero;
- it fades out as the bar's background comes in on scroll, and back at rest;
- a Normal page has no circle.

The same check on the iPhone simulator, to confirm glass is unchanged. (Not run: the glass path is untouched, since `Scrim` and `Tonal` both require no glass, and the library builds for iOS.)

## Open Questions

## Changes

- `PageActionView`: `OverContent` and `BackgroundProgress`. A `Tonal` face for `Regular` on an `Overlay` page draws a 40 dp circle in a neutral surface tone at 88 %: light `#F2F2F5`-ish under a dark icon, dark `#2B2B2E`-ish under a light one. The `Clear` scrim and the tonal circle both fade with `1 - BackgroundProgress`, in steps of 0.01. The filled visual states are rebuilt only when the fill changes, and the pressed colour scales the fill's alpha, so a faded-out circle no longer flashes black.
- `HeaderBarView`: passes `OverContent` and `BackgroundProgress` to both action views.
- `NavigationRegion`: follows the header page's `HeaderBarMode`, `ScrollEdgeProgress` and `EffectiveHeaderBarBackground`. A `Transparent` background counts as never fading in.
- Showcase "Header bar" sample: a "Button glass" group (Regular / Clear), also in the generated code.
- `docs/wiki/regions.md`: the `HeaderBarGlass` row and the Overlay header section.

Verified on the Pixel 10 Pro emulator in the Header bar sample with Overlay:
- Regular at rest shows light circles; Clear shows dark circles.
- Scrolled, both fade out to plain icon buttons on the solid bar.
- In dark mode Regular shows dark circles with white icons, and they fade the same way.

## Decisions

- **Neutral tones rather than Material's purple-tinted baseline** (`#F3EDF7` / `#211F26`): Spine has no Material colour scheme, and a lavender tint would sit oddly next to an app's own accent.
- **The circle's tone follows the icon's luminance**, not just the theme, so a fixed white foreground over a dark photo gets a dark circle.
- **`Clear` with the theme's black icon** stays a dark scrim with a black glyph, which reads poorly. That was already so before this change; `Clear` is meant to go with a white foreground.

- **Only `Overlay` pages get the tonal container**, not large-title or scroll-edge pages: those rest on the page's own background, where a container-less icon button is right for Android's top app bar.
