# Issue #377 — iOS 26 header bar is 10 pt shorter than UINavigationBar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/377
**Branch:** issue/377-ios26-header-height
**Status:** Completed
**Related:** #374 (PR #376), stage 3 (#333)

## Plan

On an iPhone 16 Pro (iOS 26), Spine's header bar ended flush with its 44-pt buttons (at ~106 pt), while a native `UINavigationBar` ends 10 pt lower (~116 pt).

1. Build a small native reference (`UINavigationController` + `UITableView`) on the iOS 26 simulator and measure the bar in a region, with a large title and in a sheet, at rest and scrolled, soft and hard.
2. Add `HeaderBarConstants.BarHeight`: 54 on iOS and Mac Catalyst 26+, `Height` elsewhere.
3. Use it wherever the header's height is derived (title row, content inset, scroll inset), keep the 44-pt item row where it is, and keep the title text centred on that row.
4. Docs (`regions.md`, `sheets.md`, `/spine-page` skill).

## Open Questions

None.

## Changes

- **Native reference (iOS 26.4 simulator, iPhone 17):**
  - Region, inline title: `navigationBar` frame y 62, height 54. The items are at the top (buttons at 62–106), the title label's centre is at 84, the table's content inset is 116.
  - Large title: the bar is 106 at rest (54 plus a 52-pt large-title row at 116–168) and 54 once collapsed.
  - Sheet (`.large()` detent, grabber): the sheet's top edge is at 62, the bar at 78 (16 below the edge), height 54, content inset 70 in the sheet.
  - Scroll edge, hard: the band ends at 116 under a collapsed large title and at the bar's bottom edge in a sheet. Under an inline title in a region (pushed or root) it ends at **106**, flush with the items. Soft fades out at about 121 in every case.
- `HeaderBarConstants.BarHeight` (new, public, documented): 54 on iOS/Mac Catalyst 26+, else `Height` (44 on older iOS, 48 on Android, 32 on Windows).
- `PagePresenter`: the title row is `BarHeight` (plus the status bar under a floating bar). The title label still fills the row for UIKit's edge effect, with 10 pt of bottom padding so its text centres on the 44-pt item row. For an inline title with `ScrollEdgeHard` in a region, the 10 pt is a bottom margin instead, so the label, and with it UIKit's hard band, ends with the items, as natively. A `Solid` background and the Android/Windows band follow the row height.
- `NavigationRegion.SafeAreaInsetsFor`: the top inset under a floating bar is status bar + `BarHeight`, so the Overlay content inset, `ScrollInset` top and the large title's start follow.
- Docs: new "Header bar height" section in `regions.md` (table per platform, what the native reference showed). The `sheets.md` and `/spine-page` skill mention `BarHeight`.

## Verified

- **iPhone 17 simulator (iOS 26.4), light and dark:**
  - Collapsing header (large title, soft/automatic): at rest the large title sits in the 116–168 row, as natively. Scrolled, the title centres at 84 on the back button, and the blur reaches ~122 (before this change: ~106).
  - Scroll edge, Hard: the band ends at 106, as natively for an inline title. Soft: the blur reaches ~122. Solid + Overlay: the solid bar ends at exactly 116. At rest, the first content starts below 116.
  - Bottom sheets page (Auto → ScrollEdge): scrolled, the blur covers the whole 54-pt bar. In a sheet, the title row is 54 and the content starts below it; the close button, title and Save are unchanged.
  - Side-by-side comparison with the native reference: `hdr-large-soft.png`, `hdr-inline-hard.png`, `hdr-sheet.png`.
- **Pixel 10 Pro emulator (Android):** Scroll edge page unchanged (`BarHeight == Height`, no padding or margin on the label).
- Mac Catalyst builds.

## Decisions

- **54 pt on iOS 26+, in sheets too; 44 before iOS 26.** UIKit's navigation bar is 54 in a region and in a sheet on iOS 26. iOS 18's is 44 (there is no iOS 18 runtime on this machine to check against, and the value is unchanged from before). Mac Catalyst 26 gets the same as iOS 26, as the issue asked. The Mac's own bar was not checked.
- **A static property, not a const.** It depends on the OS version at run time. `Height` stays the 44-pt item row (buttons and circles), which does not change.
- **The inline-title hard band stops at the items, as UIKit's does.** The native reference is unambiguous about this (pushed and root, light and dark): under an inline title the hard band ends at 106, and under a large title and in a sheet at the bar's bottom edge. Jonatan's comparison was the Collapsing header page (a large title), which now matches at 116. The layout (content start, content inset) is 54 in every case, as natively.
- **Sheets keep `SheetTopPadding` = 20.** Natively the bar starts 16 pt below the sheet's top edge; Spine's buttons start 20 below it. The issue asked to keep the item row where it is, so this is only recorded here.
- **The sample's main page hero keeps `SystemBarInsets.Top + 44`** as its collapsed height. It is a hero with a single gear, not a header bar, and it has the same number on every platform.
- **`LargeTitleCollapseDistance` is unchanged.** It is measured from the start of the scroll content, which the larger inset already moves down, so it does not depend on the bar's height.
