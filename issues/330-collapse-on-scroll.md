# Issue #330 — HeaderBarMode.CollapseOnScroll: large title that collapses into the header bar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/330
**Branch:** issue/330-collapse-on-scroll
**Status:** In Progress
**Stage:** 3 of the app-review plan (#333)

## Plan

### Gap
Orientera builds the iOS large-title behaviour by hand: a label that fades in where the header title would be, a band behind the status bar, fade offsets tuned by eye and per-platform title heights copied from Spine. Every app that wants a large title repeats that.

### Design
- `HeaderBarMode.CollapseOnScroll` on the page attribute (and `ViewModelBase.HeaderBarMode`). It lays out like `Overlay` (content starts at the top, the title row floats over it, `SafeAreaInsets.Top` is status bar plus header) but keeps the theme's title colour and adds a bar background.
- The page's own large title is the first thing in its scroll content. Spine watches the scroll offset of the page's scroll source and:
  - fades the header bar's title in as the large title passes under the bar (`CollapseDistance − LargeTitleFadeLength` → `CollapseDistance`),
  - fades the bar background from transparent to the solid theme background once content scrolls under it (0 → `ScrollEdgeFadeLength`), the iOS 26 scroll-edge / Material 3 "on scroll" moment,
  - publishes the title progress as `ViewModelBase.HeaderBarCollapseProgress` (0…1) so a page can fade a hero greeting with it.
- New public static `HeaderBar` (namespace `Plugin.Maui.Spine.Extensions`, next to `SafeArea`) with attached properties on the page: `ScrollSource` (a `ScrollView` or `CollectionView`; default the page's first one) and `CollapseDistance` (default `HeaderBarConstants.LargeTitleCollapseDistance`, where the large title's text has gone under the bar). The internal header bar view is renamed `HeaderBarView` so the public name is free in XAML.
- The scroll source gets `SafeArea.ScrollInset` Top added, so the large title starts right under the bar without the page doing anything.
- Offsets: MAUI's `Scrolled` events on every platform; on iOS / Mac Catalyst the value is read from the native `UIScrollView` as `contentOffset + adjustedContentInset.Top` so a content inset does not shift the zero point.
- Reduce Motion (iOS/Mac `UIAccessibility.IsReduceMotionEnabled`, Android animator duration scale 0, Windows `UISettings.AnimationsEnabled`): no fades, the title and background switch at the midpoint of their ranges.
- Public constants on `HeaderBarConstants`: `LargeTitleFontSize`, `LargeTitleFontAttributes`, `LargeTitleHeight`, `LargeTitleMargin`, `LargeTitleFadeLength`, `ScrollEdgeFadeLength`, per platform (iOS: 34 pt bold in a 52-point row, 16 side margin; Android: Material 3 medium top app bar, 24 sp in 56 dp, 16 margin; desktop 28).

### Steps
1. Enum value, view-model properties, `HeaderBar` attached properties, constants, reduced-motion helper.
2. Collapse tracker (source discovery, offset, progress) attached from `NavigableMeta`.
3. `PagePresenter`: floating layout for both modes, bar background, title opacity; `NavigationRegion` treats the new mode like `Overlay` for padding and insets.
4. Sample "Collapsing header" page with a code example; docs in `regions.md` and the `/spine-page` skill.
5. Build iOS, Android, Mac Catalyst; verify on both devices (slow/fast scroll, back navigation, light/dark, Reduce Motion).

## Open Questions

None.

## Changes

- `HeaderBarMode.CollapseOnScroll`, with an internal `Floats()` helper; `NavigationRegion` (padding, `SafeAreaInsets`) and `PagePresenter` (content spans both rows, title row over it) treat it like `Overlay`.
- `ViewModelBase.HeaderBarCollapseProgress` (public getter, 0…1) and internal `ScrollEdgeProgress`.
- `Extensions/HeaderBar.cs` (new): attached `ScrollSource` and `CollapseDistance` on the page, and the tracker that finds the source (explicit, first `ScrollView`/`CollectionView`, or the first one added later), adds `Top` to its `SafeArea.ScrollInset`, and turns `Scrolled` events into the two progress values. On iOS / Mac Catalyst the offset is read natively as `contentOffset + adjustedContentInset.Top`. Attached from `NavigableMeta.Apply`.
- The internal header bar view `HeaderBar` is renamed `HeaderBarView` so the public attached-property class can take the name in XAML.
- `PagePresenter`: a `BoxView` behind the title row (opaque colour faded with `Opacity`), coloured with the page's own opaque background or the platform's page background (iOS `systemBackground` for the app theme, Android window background), repainted through `SpineTheme.Track`; title opacity follows `HeaderBarCollapseProgress`.
- `Core/ReducedMotion.cs` (new): Reduce Motion on iOS/Mac, animator scale 0 on Android, `UISettings.AnimationsEnabled` on Windows, cached for a second.
- `HeaderBarConstants`: `LargeTitleFontSize`, `LargeTitleFontAttributes`, `LargeTitleHeight`, `LargeTitleSideMargin`, `LargeTitleMargin`, `LargeTitleFadeLength`, `ScrollEdgeFadeLength`, `LargeTitleCollapseDistance`; class summary.
- Sample: `Pages/Collapsing/CollapsingPage` ("Collapsing header", `windowblinds.svg`), a CollectionView whose header is the large title, text, the live progress and a code example, over 40 rows.
- Docs: "Collapsing header" section and the `HeaderBar` row in `regions.md`; `/spine-page` skill.

## Decisions

- **Layout like Overlay, not a Normal row.** With content below the bar, a solid bar in the page colour would look identical to a transparent one; content has to pass under the bar for the transition to mean anything, and the material surface of #300 needs it there too.
- **Two ranges, not one.** The background follows content reaching the bar (solid after 12 points), which is when iOS 26 shows its scroll-edge effect and Material 3 lifts its bar; the title follows the large title leaving (the last 20 points before the collapse distance). One shared offset would either leave rows visible through a transparent bar or bring the title in while the large title is still on screen.
- **Bar colour = the page's background.** Until #300 there is no material; the page colour makes the collapsed bar read as the page continuing behind the title, as the iOS 26 bar does. On Android the window background is used because it is what shows behind pages in both hosts (theme background, or the bar surface in the tab host).
- **Constants, not a LargeTitle control.** The issue asked for the size and position to come from `HeaderBarConstants`; a page keeps full control of its large title (a hero greeting, a subtitle beside it) and `CollapseDistance` covers titles that are not in the first row.
- **HeroCollectionView does not opt in directly.** It is a `CollectionView`, so the tracker already follows it as the first scrollable or as `ScrollSource`, and its package does not reference the core package. Its own `Scrolling` code collapses a hero by direction anchors, which is a different behaviour, so nothing of it is reused: the tracker needs only the offset MAUI's `Scrolled` events already carry.
- **Scroll inset added, not replaced.** Spine ORs `Top` into the source's `SafeArea.ScrollInset`, so `Bottom` set by the page stays.
- **Reduce Motion switches at the middle of each range** rather than at its start or end, so the title never appears while the large title is still fully visible and never lags once it is gone.
