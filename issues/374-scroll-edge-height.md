# Issue #374 — Scroll edge effect covers only the status bar, and its style cannot be chosen

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/374
**Branch:** issue/374-scroll-edge-height
**Status:** Completed
**Related:** #366 (PR #370), #330, stage 3 (#333)

## Plan

Follow-up to #366 from testing on an iPhone 16 Pro (iOS 26):

1. **Height.** Scrolled content was only softened under the status bar; the header row (back button and title) sat over sharp content. Find what UIKit sizes the effect to and make it cover the status bar and the whole bar, as behind a `UINavigationBar`.
2. **Style.** Let an app pick UIKit's soft or hard edge effect style, within the `HeaderBar` / `LargeTitle` / `HeaderBarBackground` API. Android and Windows get matching stand-ins.
3. **Runtime switching.** Make a page's header background changeable while the page is shown, and let the Scroll edge sample page switch between every value live.
4. Docs (`regions.md`, `/spine-page` skill).

## Open Questions

None.

## Changes

- **Cause of the short effect.** UIKit sizes the effect to the *elements* in the container view that carries `UIScrollEdgeElementContainerInteraction` (labels, images, controls), not to the container's frame. The container was the title row at the bar's full height, but the only element in it was the title `UILabel`, vertically centred at its text height, so the effect ended a few points below the text. An empty full-height view in the container does not count (tried: the effect disappears entirely). On iOS and Mac Catalyst the title label now fills the bar's height, with the text centred in it, so the effect covers the status bar and the whole bar. Other platforms keep the centred label.
- `HeaderBarBackground.ScrollEdgeSoft` and `HeaderBarBackground.ScrollEdgeHard` (new). `ScrollEdge` now uses UIKit's automatic style (soft on iPhone) instead of forcing soft. `Auto` still resolves to `ScrollEdge`. An internal `IsScrollEdge()` extension covers all three.
- `PagePresenter.Apple.cs`: sets `TopEdgeEffect.Style` from the value (automatic, soft, hard), also when only the style changes, and resets it to automatic when the interaction is removed.
- `PagePresenter.cs`: the Android/Windows stand-in for `ScrollEdgeHard` is the page colour at 96 % opacity down to the bar's bottom edge, with a one-pixel hairline in the separator colour (`#3C3C43` at 29 % light, `#545458` at 65 % dark). `ScrollEdge` and `ScrollEdgeSoft` keep the fading band from #366.
- **Runtime changes.** `ViewModelBase.HeaderBarBackground` and `HeaderBarMode` can be set while the page is shown. `NavigableMeta` stores a re-resolve step on the view model (`ReapplyHeaderBar`), which resolves `Auto` and the fallbacks again, updates `SafeAreaInsets` and starts scroll tracking when needed. `EffectiveHeaderBarBackground` is now observable. `NavigationRegion` watches the current page and re-pads its content host when the page starts or stops floating under the bar. `PagePresenter` re-lays out on `EffectiveHeaderBarBackground`.
- Sample `Pages/ScrollEdge`: a footer with a chip for each background (Auto, Solid, Clear, ScrollEdge, Soft, Hard) and an Overlay switch, both bound to the view model. Rows are coloured cards with text. Updated text and code example.
- Docs: `regions.md` (style table, element sizing, runtime changes), the `/spine-page` skill.

## Verified

- **iPhone 17 simulator (iOS 26):**
  - Bottom sheets page (`Auto`), scrolled: the blur now covers the status bar and the whole header row. The button text under the title is blurred, not sharp.
  - Scroll edge page: every value switches live. Soft fades into the bar a little past its edge. Hard shows a frosted band with a hard edge at the bar's bottom. Solid and Clear give the bar its own row. With Overlay on, Solid closes over the content, Clear shows it, and Soft blurs it. Checked in light and dark.
  - Reduce Transparency: Hard (and every scroll edge value) gives a solid bar.
  - Collapsing header page: still fine; the edge covers the bar once the title has collapsed.
- **Pixel 10 Pro emulator (Android), light and dark:** switching is live. ScrollEdge/Soft show the fading band. Hard shows the nearly opaque band with the hairline. Solid gives the bar its own row. Overlay with Clear shows content through the bar.
- Mac Catalyst builds; the Mac's automatic style was not looked at.

## Decisions

- **Enum values, not a separate style property.** `ScrollEdgeSoft` and `ScrollEdgeHard` sit beside `ScrollEdge` in `HeaderBarBackground`. The style only means something for a scroll edge background, so a fourth setting (`HeaderBarScrollEdgeStyle`) would mostly be ignored. One value also keeps the runtime switch to one property. The attribute, `options.*Defaults.HeaderBarBackground` and the view model take the new values with no new API surface.
- **`ScrollEdge` = UIKit's automatic style.** Like a navigation bar, which leaves the style to the system. On iPhone that is soft, as before. `ScrollEdgeSoft` pins soft for an app that wants it everywhere.
- **The title label is the element, not an extra view.** UIKit ignores empty views, and a transparent `BoxView` paints black on iOS. The label is already the bar's element. Filling the bar with the text centred looks the same, and gives the effect the navigation bar's height. It applies on Apple platforms only, where it matters.
- **A page with a visible bar but an empty title** has no element for UIKit to size the effect to. That is documented rather than worked around, since every region page in practice has a title.
- **Runtime switching re-resolves everything.** Changing the background may move the page between floating under the bar and sitting below it. The resolution rules for `Auto`, Reduce Transparency and pre-26 fallbacks run again, so a runtime value behaves exactly like the same value on the attribute.
- **Hard stand-in on Android/Windows has no fade below the bar.** The hard style's point is a clear edge, so the band stops at the bar with a hairline, as UIKit's does.
