# Issue #555 — Mac: a sheet's glass header button draws grey in the light theme

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/555
**Branch:** issue/555-mac-sheet-glass-button-grey
**Status:** Completed

## Plan
Found in Spine File Manager: on the Mac, a sheet's glass close button is flat grey in the app's light theme, where a native sheet's is light. Measure before changing anything:
- the button's traits
- its ancestors' alpha
- a native sheet's close button as a control

## Changes
- `BottomSheetPageExtensions.Apple.cs`: `SpineSheetViewController` gives its view `UIColor.SystemBackground` on Mac Catalyst instead of `UIColor.Clear`.

## Decisions
- **Measured, in the app showing the fault:**
  - The glass button's trait collection is Light, as are the sheet view controller, its container and the window.
  - No ancestor has an alpha below 1.
  - Apple's controls drew light: a `UIBarButtonItem.Close` in a native `UINavigationController` sheet, and a standalone `UIButton` with `GlassButtonConfiguration` in the same sheet.
  - Setting the Spine sheet view's background to the system background turned its glass button light, so the clear view behind the glass is the cause.
- **Mac only:** iPhone sheets at a partial detent are Liquid Glass themselves and keep the clear view. The Mac sheet is an opaque card, so the system background matches it in both themes (checked in dark: unchanged).
