# Issue #810 — Rows on Windows: Settings cards, a full-width switch, status as a dot

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/810
**Branch:** windows-rows
**Status:** Completed

## Plan

Make `SpineSection` and `SpineRow` on Windows look like Windows Settings (compared side by side with Settings → Display),
let a switch reach the row's edge, show `Status` the way the Mac and Windows show a connected state, and make the
header status's text small enough to match an app's own small text.

## Changes

- **Switch (core, Windows):** a `SwitchHandler` mapping drops the ToggleSwitch's 154 minimum width and, once loaded,
  collapses the template's fixed 12-point column for "On"/"Off" text. The switch is as wide as its track, so it ends at
  the row's padding like a chevron.
- **`SpineSectionStyleOptions`:** `SeparateRows` (on by default on Windows): each row a card of its own, `RowSpacing`
  (4) apart, no separators. `BorderColor` for a 1-point stroke (Windows' card stroke by default there). Windows: 4-point
  corners, `#FBFBFB` cards in light mode, a bold 14-point header in the text colour, header and footer lined up with
  the cards (`TextInset` 1), 12-point footer.
- **`SpineRowStyleOptions`:** `ShowIconBadges` (off on Windows: the glyph in `IconColor`, the text colour there, with no
  badge), `IconSpacing` (space between the icon and the text, 15 on Windows), `StatusAsDot` (on for Mac Catalyst and
  Windows) and `StatusDotSize`. Windows: rows 66 points high, a 30-point icon square (the set's 5-point margin leaves a
  20-point glyph), Windows' secondary text colour for detail and chevron, and its success and caution colours.
- **`SpineRow`:** the detail sits in a line with the status dot. With `StatusAsDot` and a detail the dot shows before
  it and the mark at the end is hidden, so the chevron rule no longer counts that status. Without a detail the row keeps
  the mark.
- **`PageAction`:** `FontFamily` and `FontSize` for a status's text (`IsStatus`); the default is 13 points instead of 15.
- Docs: `docs/wiki/rows.md`, `docs/wiki/page-actions.md`.

## Decisions

- **Monochrome glyphs on Windows**, not the badge colour as a tint: Windows Settings draws its row icons in the text
  colour. `ShowIconBadges = true` brings the badges back.
- **Status as a dot on the Mac too**: macOS Settings marks a joined network "● Connected"; iOS keeps the check, as its
  own Settings does.
- **The switch's column is changed after load**: the 12 is a literal in WinUI's template, not a theme resource, so it
  can't be overridden from resources.

## Verified

- Windows at 150 %, AIDash Connect's Settings with this build: one card per row, 20-point glyphs, switches and chevrons
  end at the same edge, the status dot before "Inloggad", headers in bold white. A first build crashed opening Settings
  (a null `Color` converted to a stroke brush); the stroke is now set only with a colour.
- Not tried: the Mac (status dot), light mode on Windows, the Showcase's Rows page.
