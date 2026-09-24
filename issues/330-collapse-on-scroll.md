# Issue #330 — HeaderBarMode.CollapseOnScroll: large title that collapses into the header bar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/330
**Branch:** issue/330-collapse-on-scroll
**Status:** Completed
**Stage:** 3 of the app-review plan (#333)

## Plan

### Gap
Orientera builds the iOS large-title behaviour by hand. It fades in a label where the header title would be, puts a band behind the status bar, tunes the fade offsets by eye and copies per-platform title heights from Spine. Every app that wants a large title repeats that work.

### Design (final, shared with #366)
The first version added `HeaderBarMode.CollapseOnScroll`. Before the PR was merged, Jonatan asked for #330 and #366 (iOS 26 scroll edge effect) to form one API, so #366 does not have to rework #330. The header bar now has three independent settings. Each one is available on the attribute and as an app-wide default in `options.RegionDefaults` / `TabDefaults` / `SheetDefaults`, using the copy-with-defaults pattern.

| Setting | Values | Decides |
|---|---|---|
| `HeaderBar` | `Normal`, `Overlay` | Layout: whether the page draws its own top under the bar |
| `LargeTitle` | `bool` | Whether the page opens on a large title that collapses into the bar |
| `HeaderBarBackground` | `Auto`, `Solid`, `Clear` (+ `ScrollEdge` in #366) | What is behind the bar while content scrolls under it |

```csharp
[NavigableTab(Title = "Inbox", LargeTitle = true)]
[NavigableRegion(Title = "Trip", HeaderBar = HeaderBarMode.Overlay, LargeTitle = true, HeaderBarBackground = HeaderBarBackground.Solid)]
options.TabDefaults.LargeTitle = true;
```

- Content scrolls under the bar ("floats") with `Overlay` or `LargeTitle`. #366 adds `ScrollEdge` as a third reason.
- `HeaderBarBackground.Auto` resolves to `Clear` under `Overlay` (the #326 behaviour is unchanged) and to `Solid` otherwise. #366 adds `ScrollEdge` (system edge effect on iOS 26, fallbacks elsewhere) and makes `Auto` resolve to it on iOS 26. Both changes are additions: no existing value or name changes meaning for a page that set it.
- `Solid` is transparent at the top and becomes the page's background once content passes under the bar, fading in over `ScrollEdgeFadeLength`. `Clear` has no background.
- There is one scroll-source mechanism for both issues. `HeaderBar.ScrollSource` is an attached property on the page. Without it, Spine uses the page's first `ScrollView` or `CollectionView`, including one added later. Spine adds `Top` to that view's `SafeArea.ScrollInset`.
- Spine publishes two progress values: `ViewModelBase.HeaderBarCollapseProgress` (the title, public) and an internal edge progress (the background). #366's `ScrollEdge` fallbacks can use the edge progress.
- Reduce Motion switches the title and the background instead of fading them. #366 adds Reduce Transparency: `ScrollEdge` then falls back to `Solid`.
- `HeaderBarConstants` has the large-title numbers per platform: iOS 34 pt bold in a 52-point row, Android 24 sp in 56 dp (Material 3 medium top app bar), Windows 28. Side margins are 16. `LargeTitleCollapseDistance` is (row + font) / 2, the offset at which the title's text has gone under the bar.

## Open Questions

None.

## Changes

- `HeaderBarBackground` enum (`Auto`, `Solid`, `Clear`) next to `HeaderBarMode`. The attribute, `NavigableDefaults` and `ViewModelBase` gain `LargeTitle` and `HeaderBarBackground`. `NavigableMeta` copies them. `ViewModelBase` gets the internal `HeaderBarFloats`, `EffectiveHeaderBarBackground` and `FollowsScroll`.
- `ViewModelBase.HeaderBarCollapseProgress` (public getter) and the internal `ScrollEdgeProgress`.
- `Extensions/HeaderBar.cs` (new):
  - Attached properties `ScrollSource` and `CollapseDistance`.
  - A tracker that finds the source: the explicit one, else the first `ScrollView`/`CollectionView`, else the first one added later.
  - The tracker adds `Top` to the source's scroll inset and turns `Scrolled` events into the two progress values.
  - On iOS and Mac Catalyst the offset is `contentOffset + adjustedContentInset.Top`, read natively.
  - `NavigableMeta.Apply` attaches the tracker when the page follows scroll.
- The internal header bar view is renamed from `HeaderBar` to `HeaderBarView`, so the public class can take the name in XAML.
- `NavigationRegion` pads and reports insets for floating headers (`Overlay` or `LargeTitle`).
- `PagePresenter`:
  - A `BoxView` behind the title row, in an opaque colour faded with `Opacity`, shown for a floating header whose background resolves to `Solid`.
  - Its colour is the first opaque background from the page up through the host page. Without one, it falls back to iOS `systemBackground` for the app theme, or the Android window background. It is repainted via `SpineTheme.Track` and again when the fade starts.
  - For `LargeTitle`, the title's opacity follows the collapse progress.
- `Core/ReducedMotion.cs` (new): Reduce Motion on iOS/Mac, animator duration scale 0 on Android, `UISettings.AnimationsEnabled` on Windows. Cached for a second.
- `HeaderBarConstants`: `LargeTitleFontSize`, `LargeTitleFontAttributes`, `LargeTitleHeight`, `LargeTitleSideMargin`, `LargeTitleMargin`, `LargeTitleCollapseDistance`, `LargeTitleFadeLength`, `ScrollEdgeFadeLength`, and a class summary.
- Sample: `Pages/Collapsing/CollapsingPage` ("Collapsing header", icon `windowblinds.svg`, `LargeTitle = true`). It shows a large title, an explanation, the live progress and a code example above 40 rows.
- Docs: a "Large title" section in `regions.md` with the three-setting table, two attribute rows, and the `/spine-page` skill.
- Verified on iPhone 17 (iOS 26), before and after merging master:
  - Top state.
  - Slow drag: progress 0.15 → 0.65, title fading in while the large title passes under the solid bar.
  - Fast fling down and back.
  - Back navigation.
  - Dark mode: the bar takes the page's own `#1f1f1f` background.
  - Reduce Motion: the same offset that gives 0.25 normally gives 0.00, with the background already solid.
- Verified on the Pixel 10 Pro emulator (Android):
  - Top state, mid-collapse at 0.58, fast fling to the end, and the header back button.
  - Dark and light repaint.
  - "Remove animations": progress jumps straight to 1.00.
  - The Overlay sample is unchanged (`Auto` → `Clear`).
- Built for Mac Catalyst.

## Decisions

- **Three independent settings instead of more `HeaderBarMode` values.** Layout, large title and background combine: a hero page with a greeting that collapses and a bar that turns solid is `Overlay + LargeTitle + Solid`. As modes that would be one enum value per combination, and #366's `ScrollEdge` would double them.
- **`LargeTitle` implies content under the bar.** A large title that scrolls away has to pass under the bar, so there is no layout choice to make for it.
- **`Auto` keeps `Overlay` clear.** #326 pages draw their own top and often have a fixed white foreground; a solid page-coloured bar would hide their title. Opting in with `Solid` is explicit.
- **Two ranges, not one.** The background follows content reaching the bar and is solid after 12 points, which is when iOS 26 shows its edge effect and Material 3 lifts its bar. The title follows the large title leaving. With one shared offset, rows would either show through a transparent bar or the title would come in while the large title is still visible.
- **Default collapse distance is (row + font) / 2.** With the whole row height, the bar title only appeared after an empty row had also scrolled away, which was visibly late on iOS. At (row + font) / 2 it fades in while the text itself passes under the bar.
- **Bar colour is the page's background.** There is no material until #300 or #366. The page's own colour reads as the page continuing behind the title, the way the iOS 26 bar does. The colour is searched up through the host page because apps style `ContentPage` (the sample does). The platform colour is only the fallback.
- **Constants, not a LargeTitle control.** The issue asked for the size and position to come from `HeaderBarConstants`. The page keeps full control of its large title, and `CollapseDistance` covers titles that are not in the first row, such as a hero greeting.
- **HeroCollectionView does not opt in directly.**
  - It is a `CollectionView`, so the tracker already follows it as the first scrollable or as `ScrollSource`.
  - Its package does not reference the core package.
  - Its own `Scrolling` code collapses a hero with direction anchors, which is a different behaviour, so none of it is reused. The tracker only needs the offset that MAUI's `Scrolled` events already carry.
- **The scroll inset is added, not replaced.** Spine ORs `Top` into the source's `SafeArea.ScrollInset`, so a `Bottom` set by the page stays.
- **Reduce Motion switches at the middle of each range.** The title never appears while the large title is still fully visible, and never lags once it is gone.
- **Android reads the animator duration scale setting directly.** The first cache check compared against `long.MinValue`, overflowed, and never read the setting. It is fixed with an explicit "read yet" flag, which the Android verification caught.
