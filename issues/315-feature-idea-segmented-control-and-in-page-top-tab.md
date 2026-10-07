# Issue #315 — Feature idea: Segmented control and in-page top tabs

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/315
**Branch:** issue/315-feature-idea-segmented-control-and-in-page-top-tab
**Status:** In Review

## Plan

There is no study for this issue, so the plan follows the issue and the patterns of the other native controls in the core package (`Lightbox`, `MenuButton`, `ContextMenu`): a MAUI `View` with a handler per platform, registered in `ConfigureHandlers`, icons as SVG resource names rendered by `MenuButton.Icon`, and the accent from `SpineTheme.GetAccent` repainted through `SpineTheme.Track`.

Work done unattended overnight; every choice Jonatan could want to change is listed under **Decisions**.

### 1. `SegmentedControl` (core, `Plugin.Maui.Spine.Presentation`)
- `Presentation/SegmentedControl.cs`: the view.
  - `Segments` (content property): `Segment` elements with `Title`, `Svg`, `IsEnabled`. Segments are logical children, so `{Binding}` on a segment works.
  - `SelectedIndex` (two-way), `SelectionChanged` event.
  - `SelectedSegmentColor` (`Color?`): the selected segment's fill.
- Handlers:
  - **iOS / Mac Catalyst** (`SegmentedControl.Apple.cs`): `UISegmentedControl`. A segment shows its title, or its icon when it has no title; with both, the icon and the title are drawn into one image. Glass on iOS 26 comes from UIKit.
  - **Android** (`Platforms/Android/SegmentedControl.Android.cs`): Material `MaterialButtonToggleGroup` of outlined `MaterialButton`s (single selection, selection required): the Material 3 segmented button. Checked segment filled with the accent at container strength and a check mark when the segment has no icon.
  - **Windows** (`Platforms/Windows/SegmentedControl.Windows.cs`): WinUI `SelectorBar`.
- Registered in each platform's `ConfigureHandlers`.

### 2. `TopTabs` (thin composition)
- `Presentation/TopTabs.cs`: a `Grid` with a `SegmentedControl` on top and a content area.
  - `Tabs` (content property): `TopTab` with `Title`, `Svg`, `Content` (a view) or `ContentTemplate` (a `DataTemplate`, built on first show).
  - `SelectedIndex` (two-way).
  - A tab's content is created the first time it is shown and kept afterwards (hidden, not removed), so scroll positions survive a switch.
  - When the page has not set `HeaderBar.ScrollSource` itself, `TopTabs` points it at the visible tab's first `ScrollView`/`CollectionView`, and gives a tab's list the page's `ScrollInset` when it is created after the page was set up.

### 3. Showcase, docs, tests
- Showcase page `Pages/Segmented` ("Segmented control"): text segments bound to a view model, icon segments, a disabled segment, the accent override; a button to a full-page `TopTabs` sample (Class / Club / Me lists).
- `docs/wiki/segmented-control.md`, linked from the wiki index and README; a line in the `/spine-controls` skill.
- No unit tests: the controls are thin MAUI views over native handlers, with no logic that runs without MAUI and a platform (see Decisions).

### Out of scope for v1 (follow-ups)
- Swipe between tabs on Android (`ViewPager2` + `TabLayout`).
- `ItemsSource` / templated segments.

## Open Questions

None blocking; see Decisions.

## Changes

- `Presentation/SegmentedControl.cs`: `SegmentedControl` (`Segments`, two-way `SelectedIndex` coerced to ≥ -1, `SelectedSegmentColor`, `SelectionChanged`), `Segment` (`Title`, `Svg`, `IsEnabled`; logical children so they bind), `SegmentSelectedEventArgs`. Repaints through `SpineTheme.Track`; a user pick plays `SpineOptions.Haptics.TabSwitch`.
- `Presentation/SegmentedControl.Apple.cs`: `UISegmentedControl` handler. Title, icon, or both drawn into one template image (accessibility label = title, icon on the trailing side in RTL; recomposed when `FlowDirection` changes). `SelectedSegmentColor` sets `SelectedSegmentTintColor` and a black/white selected title.
- `Platforms/Android/SegmentedControl.Android.cs`: `MaterialButtonToggleGroup` of outlined `MaterialButton`s styled in code (40 dp, pill ends, M3 outline colours, check mark from Spine's `check.svg` on a picked segment without an icon, accent tonal fill, explicit colour as solid fill). `MeasureWithLargestChildEnabled` plus an exact re-measure in `PlatformArrange`, so segments are equal: widest × count when wrapping, shared equally when filling. Per-segment enabled state applied after the group's (the group passes its own on to every button).
- `Platforms/Windows/SegmentedControl.Windows.cs`: `SelectorBar` handler with `ImageIcon`s in the theme's text colour.
- Handlers registered in the three `ConfigureHandlers`.
- `Presentation/TopTabs.cs`: `TopTabs` (`Tabs`, `SelectedIndex`, `SelectedSegmentColor`, `BarMargin`, `SelectionChanged`) and `TopTab` (`Title`, `Svg`, `IsEnabled`, `Content`, `ContentTemplate`). Lazy add on first pick, kept hidden afterwards; owns `HeaderBar.ScrollSource` unless the page set it; gives late-built lists the page's `ScrollInset`.
- New symbol `Segmented.svg` (via `/spine-symbol`, 224 icons now; counts updated by the script).
- Showcase: `Pages/Segmented/SegmentedPage` ("Segmented control" in `SampleIndex`) and `TopTabsPage` (Class / Club / Me results; Me built from a template on first pick).
- Docs: `docs/wiki/segmented-control.md` with iOS and Android screenshots, README rows, a pointer from `tab-host.md`, a section in the `/spine-controls` skill.

## Decisions

- **Core package, not a new `Controls.*` package.** Like `Lightbox`, `MenuButton` and `ContextMenu`, these wrap native controls and need nothing beyond Spine's dependencies; the `Controls.*` packages hold code-drawn controls.
- **Plain bindable properties, no `SpineStyleOptions`.** That base is for Spine's code-drawn controls; a native control is styled through its properties and ordinary MAUI styles.
- **Apple keeps UIKit's neutral thumb by default; Android uses the accent.** The native look on iOS 26 is the neutral (glass) thumb, and Apple's own apps do not tint it; Material 3 segmented buttons take their container from the theme, so Android takes a tone of `IThemeService.Accent` (20 % light, 32 % dark; Material's baseline `#E8DEF8`/`#4A4458` when the app has no accent). `SelectedSegmentColor` fills the picked segment on both. Bind `{DynamicResource Accent}` to follow the accent on iOS too.
- **Android: `MaterialButtonToggleGroup` (M3 segmented buttons), not `TabLayout`.** It is Material's segmented control; `TabLayout` is the underlined tab strip, which belongs with swipeable pages (follow-up). The buttons are styled in code because MAUI's app theme is Material Components, not Material 3.
- **Icon and title on Apple are drawn into one image.** `UISegmentedControl` shows a title or an image per segment. The composed image uses 13 pt medium, so it does not turn semibold when picked as a plain title does.
- **Equal segment widths everywhere,** matching `UISegmentedControl`: widest × count when the control wraps its content, equal shares when it fills.
- **Windows ignores `SelectedSegmentColor`** in v1; `SelectorBar`'s pill uses the system accent.
- **Haptics:** a user pick plays `SpineOptions.Haptics.TabSwitch` (off by default), the same option as the tab host; a change from code plays nothing.
- **TopTabs keeps built tabs in the tree, hidden,** rather than swapping them out, so scroll positions and state survive without relying on handler reconnects. `TopTab.Content` declared inline is built with the page (only its handlers wait); `ContentTemplate` defers the view itself.
- **TopTabs owns `HeaderBar.ScrollSource` only while the page has not set it** (or still holds the value TopTabs set). A tab without a list sets it to null, so a floating header falls back to the page's first list, which may be a hidden tab's. Acceptable for v1; listed as a follow-up.
- **No swipe between tabs in v1,** on any platform. Switching is instant, as between segments on iOS.
- **No unit tests:** there is no logic here that runs without MAUI and a platform handler; the test projects compile MAUI-free files only. Behaviour was checked with a harness on the simulator and the emulator instead.
- **Showcase icon:** variant "e" of three drafts (a rounded frame, first segment filled, one divider) was picked without Jonatan; easy to redraw.
