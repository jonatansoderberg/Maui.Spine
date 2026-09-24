# Issue #366 — Content scrolls under the header bar with the iOS 26 scroll edge effect

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/366
**Branch:** issue/366-scroll-edge (from issue/330-collapse-on-scroll)
**Status:** Completed
**Related:** #330 (PR #367), #326, #300; stage 3 (#333)

## Plan

### Gap
In native iOS 26 apps, content scrolls under the navigation bar and stays half visible behind a soft, blurred edge. Spine's header bar is a MAUI row above the content, so pages look flat next to system apps.

### Design
The API was designed together with #330 and posted on both issues. Three independent settings apply to the header bar: `HeaderBar` (layout), `LargeTitle` and `HeaderBarBackground`. This issue adds the background value `ScrollEdge`, and `Auto` resolves to it on iOS 26:

- **iOS / Mac Catalyst 26.** The page is laid out under the bar and its scroll source gets the top inset. The title row gets a `UIScrollEdgeElementContainerInteraction` (edge `Top`) pointing at the scroll view, and the view's `TopEdgeEffect` uses the soft style, so UIKit draws the effect.
- **`Auto`** resolves to `ScrollEdge` only for a region or tab page with a visible header bar whose scroll view fills the page from the top. Every container between the page and the list either holds only the list, or is a grid in which the list spans all rows. Otherwise `Auto` stays `Solid`, so a page with fixed content above its list does not end up with that content under the bar. Under `Overlay`, `Auto` is still `Clear`.
- **Explicit `ScrollEdge`** works on any page and platform:
  - Android and Windows get a stand-in band in the page colour (90 % opaque behind the bar, fading out over 24 points below it), which fades in with the scroll edge progress from #330.
  - iOS and Mac Catalyst before 26 get `Solid`.
- **Reduce Transparency** (iOS/Mac), or transparency effects off (Windows), gives `Solid`.

## Open Questions

None.

## Changes

- `HeaderBarBackground.ScrollEdge`. `Auto` is documented for iOS 26.
- `NavigableMeta.ResolveBackground` resolves `Auto` and the fallbacks when the attribute is applied, and stores the result on `ViewModelBase.EffectiveHeaderBarBackground` (now set, not computed). It also exposes `HasSystemScrollEdge` and the `FillsFromTop` rule.
- `ViewModelBase`: `HeaderBarFloats` includes `ScrollEdge`. `FollowsScroll` covers every background except `Clear`. The internal `HeaderBarScrollSource` is published by the tracker from #330.
- `Core/ReducedTransparency.cs` (new).
- `PagePresenter` is now partial:
  - The band fallback is a `LinearGradientBrush` on the existing background `BoxView`, spanning both rows at bar height plus the fade.
  - `PagePresenter.Apple.cs` (new) installs and removes the interaction. It retries when either the title row or the scroll source gets a platform view.
- `SafeAreaExtensions.Apple.cs`: a scroll view resting at its top stays at its new top when the inset changes. UIKit keeps the offset, so a `ScrollView` that got its top inset after its first layout rested with its first rows under the bar. The fix is cherry-picked onto #330's branch too.
- Sample: `Pages/ScrollEdge/ScrollEdgePage` ("Scroll edge", icon `water.svg`), with coloured rows and a code example. The Collapsing header page's text now describes `Auto` on iOS 26.
- Docs: a "Scroll edge" section and the `HeaderBarBackground` row in `regions.md`, and the `/spine-page` skill.

## Verified

- **iPhone 17 simulator (iOS 26):**
  - Scroll edge page: at rest, the first row sits below the bar. Scrolled, the rows blur and soften under the status bar and title, as behind a `UINavigationBar`. Checked in light and dark.
  - Reduce Transparency: the bar turns solid.
  - Bottom sheets page (a `ScrollView` page with no attribute changes): `Auto` gives it the edge effect, and its top is clear at rest after the inset fix. A sheet opened from it keeps its normal header.
  - Collapsing header page: the large title slides under the edge while the bar's title fades in.
- **Pixel 10 Pro emulator (Android), light and dark:** the explicit `ScrollEdge` shows the band, with the row under it faintly visible and a fade below the bar. The emulator had to be restarted (system_server ANR under host load).
- Mac Catalyst builds.

## Decisions

- **The title row is the interaction's container.** It spans the bar's width at the bar's height and holds the title; the actions float in the same band. The effect covers the status bar and the bar, as in system apps.
- **`Auto` needs the list to fill the page from the top.** Turning the effect on for every page on iOS 26 would slide fixed headers, filters and forms under the bar. The rule is structural (containers between page and list) because it runs before layout.
- **Resolved at navigation.** Reduce Transparency and the scroll-source structure are read when the page is navigated to, like the other attribute values. A page shown again after the setting changes picks it up on its next navigation.
- **Sheets keep `Solid` under `Auto`.** A sheet has its own grabber and card edge, and UIKit sheets do not show the navigation-bar edge effect there by default.
- **No blur fallback on Android and Windows.** Real blur behind a view needs snapshots (#300). The tinted, fading band is the honest stand-in, and it only appears when a page asks for `ScrollEdge`. Android's `Auto` stays `Solid`, so existing Android pages are unchanged.
- **iOS 17–18 use `Solid`, not a `UIBlurEffect`.** The system material is #300's subject, and a material band without UIKit's edge shaping would look different from iOS 26 anyway.
