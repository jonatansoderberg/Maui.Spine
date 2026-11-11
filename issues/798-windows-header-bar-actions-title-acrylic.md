# Issue #798 — Windows header bar: actions sit low under an app's ImageButton minimum, title off the page margin, Acrylic nearly opaque

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/798
**Branch:** issue/798-windows-header-bar-actions-title-acrylic
**Status:** Completed

## Plan

Found in AIDash Connect on Windows (Spine 0.261111.1530) with the header bar in the caption row
(`RegionDefaults.IsTitleBarVisible = false`).

1. Header actions centre 6 points below the caption buttons' glyphs.
2. A left-aligned title starts 8 points in, not at the page's content margin.
3. `WindowBackdrop.Acrylic` looks like a solid grey.

## Changes

- `PageActionView`: the header's `ImageButton` sets `MinimumHeightRequest` / `MinimumWidthRequest` 0. MAUI's template
  `Styles.xaml` gives every `ImageButton` a 44-point minimum; in Windows' 32-point row the WinUI button was 44 high,
  top-aligned, and its glyph centred at 22 instead of 16 (measured in the WinUI tree: `Button` 46 × 44, `Image` at y = 6).
- `PagePresenter.EmptySideInset`: a side of the bar without an action keeps `HeaderBarConstants.PageMargin` on Windows,
  as it already did with glass buttons on iOS, so a left-aligned title starts at 16, where the page's content and a
  WinUI title bar's title start (was `TitleEdgePadding`, 8).

## Decisions

- **The minimum is cleared on every platform, not just Windows.** The header button is its slot's size everywhere; the
  minimum only mattered where the slot is under 44 points (Windows' 32, the Mac's compact 34). iOS glass buttons are 44
  and Android's 48, unchanged.
- **The title margin only on Windows.** Android's Material title also starts at 16 without a navigation icon, but that
  was not checked here; left as it was.
- **Acrylic left as it is (item 3 dropped).** On the PC where it was reported (a MacBook Pro under Boot Camp) Windows runs
  on the Microsoft Basic Display Adapter, and without a GPU driver DWM draws no Mica or Acrylic for any app: both show
  their solid fallback colour (#2C2C2C base, #545454 thin, measured). A thinner acrylic (`DesktopAcrylicKind.Thin`) may
  match the Mac's backdrop better, but it could not be seen here, so the default stays until it is compared on a machine
  that draws materials.

## Verified

- Windows, AIDash Connect with this branch's `Plugin.Maui.Spine.dll`: the header icons' `Image` at y = 0 in the 32-point
  row, on the caption buttons' line on screen; the title at x = 16, flush with the cards' edge.
- Not verified: the Mac (the compact buttons now 34 points high instead of 44, glyph size unchanged), Android.
