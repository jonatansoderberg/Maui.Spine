# Issue #654 — Header bar: transient glass for page actions

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/654
**Branch:** issue/654-header-bar-transient-glass
**Status:** Completed

## Plan

AIDash Connect (a Mac Catalyst dashboard) wants its header buttons as plain icons that only show glass while
pressed. `GlassStyle.Transient` already does that for buttons with `Glass.Style`; the header bar only offered
`Regular` and `Clear`. Add `HeaderBarGlass.Transient` and map it in `PageActionView.ApplyGlass`.

## Changes

- `HeaderBarGlass.Transient`.
- `PageActionView.ApplyGlass`: non-prominent actions (and the back button) get `GlassStyle.Transient`; a prominent
  action keeps `GlassStyle.Prominent`. Without glass it is neither the scrim nor the tonal circle: a bare icon.
- Docs: `regions.md` (the `HeaderBarGlass` row), `page-actions.md` (Liquid Glass section), the attribute's XML doc.

## Verified

- AIDash Connect on Mac Catalyst 26 with this branch packed as 0.1.19-dev.1 and
  `options.RegionDefaults.HeaderBarGlass = HeaderBarGlass.Transient`: the refresh, device and settings buttons show
  as bare icons at rest.
- Not checked: the pressed glass in that app (it can only be seen while pressing), iOS 26, Android/Windows.
