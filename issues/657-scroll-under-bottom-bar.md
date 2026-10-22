# Issue #657 — Scroll under the bottom bar by default when the page's scroll view reaches the bottom

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/657
**Branch:** issue/657-scroll-under-bottom-bar
**Status:** Completed

## Plan

Pages default to `SafeAreaEdges.All` and `ScrollInset = None`, so a scrolling page stops above the home indicator and
its content is cut off there while scrolling. Apps had to opt in per page or in their defaults, and kept forgetting
(AIDash Connect on iOS 27). Make it the default for pages that leave their edges to Spine and whose scroll source
reaches the bottom edge.

## Changes

- `NavigableDefaults.ScrollUnderBottomBar` (default `true`) on the region, tab and sheet defaults.
- `NavigableAttribute.ScrollsUnderBottomBar` (internal, resolved): on when the page sets neither `SafeAreaEdges` nor
  `ScrollInset` and the defaults keep `All` / `None`.
- `NavigableMeta.ScrollUnderBottomBar`: for such a page whose scroll source (`HeaderBar.ScrollSource` or the first
  `ScrollView`/`CollectionView`) reaches the bottom (`FillsToBottom`, the counterpart of `FillsFromTop`), takes
  `Bottom` out of the page's `SafeAreaEdges`, adds it to `ScrollInset` and to the source's `SafeArea.ScrollInset`.
  A view with its own `SafeArea.ScrollInset` keeps it; sources Spine set are remembered so a page shown again keeps it.
- Docs: `regions.md` (table and Scrolling under a bar), `tab-host.md`, the `spine-page` skill, CLAUDE.md.

## Verified

- iOS 26.4 simulator (iPhone 17 Pro), Showcase with its manual `RegionDefaults.SafeAreaEdges`/`ScrollInset` removed:
  the Mesh backgrounds page (default edges, `SampleLayout` with a nested `ScrollView`) scrolls under the home indicator
  and its last block ends clear of it.
- Not checked: Android, a tab page under the floating tab bar, a sheet.
