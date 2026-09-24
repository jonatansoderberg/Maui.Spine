# Issue #379 — Header bar: backgrounds that look different, a simpler API and one sample page

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/379
**Branch:** issue/379-header-bar-page
**Status:** Completed
**Related:** #330, #366, #374, #377; build warnings in #380

## Plan

Feedback from an iPhone 16 Pro (iOS 26.5): "No difference between Solid and Clear, nor between ScrollEdge and ScrollEdgeHard. What is Auto?"

### Findings

- **Solid = Clear under `Normal`.** The presenter paints a background only when the bar floats over the content (Overlay, a large title or a scroll edge value). Under `Normal` the bar has its own row, nothing is ever under it, and the value is ignored. Solid also starts transparent and only fades in once content scrolls under the bar, so under Overlay at rest it looks like Clear too.
- **ScrollEdge = ScrollEdgeSoft on the iOS 26.4 simulator, but looks like Hard on the phone (iOS 26.5).** `ScrollEdge` sets `UIScrollEdgeEffectStyle.Automatic` and leaves the choice to UIKit, which may differ between OS versions and contexts. Side by side on the simulator, Automatic and Soft are pixel-identical and Hard differs.
- **Auto** resolves to `Clear` under Overlay, to `ScrollEdge` on iOS 26 for a region/tab page whose list fills it from the top, and to `Solid` otherwise.
- **None of this API is released.** `HeaderBarMode`, `HeaderBarBackground` and `StatusBarStyle` are not in v0.1.11, the latest tag. The sample uses project references. Values can be renamed and removed without `[Obsolete]`.

### Design

`HeaderBar` (layout), `LargeTitle` and `HeaderBarBackground` stay three independent settings, but each gets one meaning that holds in every combination:

- **Layout** decides where the page's content starts at rest.
  - `Normal`: below the bar. When content scrolls under the bar, Spine insets the scroll view so its first row still starts below the bar.
  - `Overlay`: at the top of the screen, behind the status bar and the bar. The page keeps clear what it wants with `SafeAreaInsets` / `SafeArea.ScrollInset="Top"`; Spine does not add a top inset (it did for Overlay + Solid, which pushed a photo in the scroll content below the bar).
- **Background** decides what is behind the title and the actions when content is under the bar. Five values, each visibly different:
  - `Auto`: what the platform's own bar does (see the table in `regions.md`).
  - `Solid`: the page's colour; content under the bar is hidden.
  - `Transparent` (was `Clear`): nothing; content shows through the bar. Under `Normal` content now scrolls under the bar, so it differs from `Solid`.
  - `ScrollEdge`: the iOS 26 scroll edge effect, **soft** style (was the system's automatic style).
  - `ScrollEdgeHard`: the hard style.
  - `ScrollEdgeSoft` is removed (it is what `ScrollEdge` is now).
- `EffectiveHeaderBarBackground` becomes public (read-only), so a page can show what `Auto` resolved to.
- `LargeTitle` and `StatusBarStyle` can change while the page is shown, like `HeaderBarMode` and `HeaderBarBackground`.

### Sample

One "Header bar" page replaces Collapsing header, Scroll edge and Overlay header: a photo at the top of the list, then the explanation, then coloured cards. A footer with chips for layout, large title, background, foreground and status bar style changes the page live; the page shows what each choice does, what `Auto` resolved to, and the code for the chosen combination.

### Docs

`regions.md`: a table "value → iOS 26 / iOS 18 / Android / Windows → when to pick it", screenshots. The `/spine-page` skill. Comments on #366 and #374.

## Open Questions

None.

## Changes

- `HeaderBarBackground`: `Auto`, `Solid`, `Transparent` (was `Clear`), `ScrollEdge`, `ScrollEdgeHard`. `ScrollEdgeSoft` is removed. Every value is documented by how it looks and when to pick it; the enum says the background only shows when content is under the bar.
- `PagePresenter.Apple.cs`: `ScrollEdge` sets `UIScrollEdgeEffectStyle.SoftStyle`, never `AutomaticStyle`.
- `ViewModelBase`:
  - `HeaderBarFloats` is true for Overlay, a large title, or any background but `Solid`, so `Transparent` under `Normal` lets content scroll under the bar.
  - `FollowsScroll` is gone; Spine tracks the scroll source whenever the page floats.
  - `EffectiveHeaderBarBackground` is public (read-only), defaulting to `Solid`.
  - `LargeTitle` changes re-resolve the header, like `HeaderBarMode` and `HeaderBarBackground`.
- `NavigableMeta.ResolveBackground`: `Auto` under Overlay is `Transparent`. A page without a visible header bar gets `Solid`.
- `HeaderBar` tracker:
  - It adds `Top` to the scroll source's `SafeArea.ScrollInset` only under `Normal`, remembers that it did, and takes it away again when the page switches to Overlay or stops floating.
  - On iOS it follows the native `contentOffset` through KVO. MAUI's `CollectionView` raises `Scrolled` only while one of its items is on screen, so a page whose header fills the screen left the bar collapsed (or solid) after scrolling back to the top.
- `NavigationRegion`: re-pads the content host when `LargeTitle` changes, and applies `StatusBarStyle` when the shown page changes it.
- Warnings in the files #380 left out:
  - `PagePresenter`: the private `Background` property is now `BarBackground` (CS0108). `TitleSlotLayout` implements `ILayoutManager` explicitly (CS0108). The resolved system colour has a fallback (CS8603).
  - Doc comments: `HeaderBarConstants` (CS1591), `NavigationRegion` (CS1734), `SpineOptions` (CS1574).
  - Sample: `Item` inherits `ObservableObject` with partial properties (MVVMTK0033). The main page uses `{PageCommand ItemTapped}` (MAUIG2045).
  - Packages and the sample (iOS, Android, Mac Catalyst) now build with 0 warnings locally.
- Symbol `HeaderBar` (a phone with a bar at the top, the `Sheet` symbol turned over). `SpineIcons` regenerated; the set has 220 icons.
- Sample: `Pages/HeaderBar/HeaderBarPage` ("Header bar", `headerbar.svg`) replaces `Pages/Collapsing`, `Pages/Overlay` and `Pages/ScrollEdge`.
  - A photo, then the selected choices with an explanation of each, then what `Auto` resolved to, the code for the combination, and 30 coloured cards.
  - A footer of chips for layout, large title, background, foreground and status bar style changes the view model's properties live.
- Docs:
  - `regions.md` has one "Header bar" section: the three settings, a "Backgrounds" table (value → iOS 26 / before 26 / Android / Windows → pick it for), and three screenshot strips (iOS light and dark, Android, Overlay). The Overlay, Large title, Scroll edge, live-change and height subsections are rewritten for the new rules.
  - The `/spine-page` skill.

## Verified

- **iPhone 17 simulator (iOS 26.4)**, driven by a scratch-pad harness (a `ModuleInitializer` added with `CustomBeforeMicrosoftCommonTargets` that reads commands from a file in the app's container, so no sample code carries it):
  - Every background × Normal/Overlay × at rest/scrolled, light and dark. All five values look different under `Normal` once cards are under the bar: Auto (= soft edge here), Solid, Transparent, soft and hard. Under Overlay at rest, `Solid` is transparent over the photo and turns solid on scroll.
  - Large title on/off, live, under Normal and Overlay with Auto and Hard.
  - Scrolling back to the top (fling and programmatic) resets the bar's progress to 0. Before the KVO change it stayed at 1.
  - Reduce Transparency: `ScrollEdgeHard` resolves to `Solid`. Reduce Motion: the background switches at 6 of its 12 points.
- **Pixel 10 Pro emulator (Android)**, same matrix in light and dark: `Auto` = `Solid`, the soft band, the hard band with hairline, Transparent content through the bar. A fling back to the top resets the progress.
- Mac Catalyst builds. Windows compiles in CI only.

## Decisions

- **No `[Obsolete]` aliases.** `HeaderBarMode`, `HeaderBarBackground` and `StatusBarStyle` are not in any released package (v0.1.11 is the latest tag), and the sample references the projects. Renaming `Clear` and removing `ScrollEdgeSoft` breaks nobody.
- **Five values, style kept in the enum.** A separate `HeaderBarScrollEdgeStyle` property would be a fourth setting that means nothing for three of the five backgrounds. `ScrollEdge` is the soft style every platform shows by default; the rarer hard style gets the longer name.
- **`Transparent`, not `Clear`.** It matches MAUI's `Colors.Transparent`. "Clear" read like "no bar" and did nothing visible under `Normal`.
- **`Transparent` under `Normal` floats the page.** Before, `Solid` and `Clear` under `Normal` were the same bar in its own row. Now every value shows its difference once content scrolls under the bar. `Solid` under `Normal` keeps its own row: it looks the same as floating with an opaque bar and needs no scroll tracking.
- **`Auto` stays "the platform's own bar".** Collapsing it to one value would either turn the iOS 26 effect off, or slide fixed content under the bar on every page. The explanation is now one sentence plus a table, and the sample and `EffectiveHeaderBarBackground` show what it resolved to.
- **No top inset under Overlay.** Overlay means the page draws its own top. Adding the inset (as Overlay + Solid did) pushed a photo inside the list below the bar. Spine only removes an inset it added itself, so a `SafeArea.ScrollInset="Top"` set by the page stays.
- **`ScrollEdge` never uses the automatic style.** On the simulator (26.4) automatic resolved to soft. On Jonatan's iPhone (26.5) `ScrollEdge` and `ScrollEdgeHard` looked the same. Pinning soft makes the value mean the same on every OS version.
- **KVO on iOS, MAUI's event elsewhere.** Android's `RecyclerView` listener reported every scroll in testing, including back to the top.
- **The sample's chips wrap** instead of scrolling sideways, so all five backgrounds are visible at once.

