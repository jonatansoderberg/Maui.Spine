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

## Follow-up: matched to Settings pixel by pixel

Measured from screenshots of Windows Settings → System and AIDash Connect's Settings side by side at 150 % (card edges,
ink boxes of icon, title, detail and chevron, colours):

- Card fill `#0DFFFFFF` (light `#B3FFFFFF`), translucent like `CardBackgroundFillColorDefault`, so Mica shows through.
  The stroke is `#33000000` (light `#17000000`): MAUI's fill runs under the stroke, where WinUI's stops inside it, so
  Windows' 10 % only cancelled the fill; 20 % gives the same dark edge on Mica.
- Rows 69 points (104 px with the stroke, as Settings' cards).
- `IconPadding` (new; 1 on Windows) in a 26-point square: the glyph is 20 points and centred where Settings centres its
  icon. `IconSpacing` 15: the title starts 60 points into the card.
- Chevron in the title colour, a heavier stroke (`GlyphLineWidthScale` 2) and 22 points from the card's edge.

After this: icon, title, detail and chevron within a pixel of Settings; heights and the gap equal. The page behind
is the app's backdrop: AIDash moved to Mica on Windows, as Settings.

## Follow-up: sharp SVGs on Windows

Row icons read light grey next to Settings' white ones, and looked upscaled. Pixel by pixel they were soft over two to
three pixels: `SvgBitmapImageSourceService` (Windows) decoded the bitmap with `DecodePixelType.Logical` to the point
size, and a bitmap decoded before it is in a window gets scale 1: the 39-pixel render of a 26-point icon at 150 % was
decoded to 26 pixels and stretched back to 39. It now decodes to the bitmap's own pixels (`Physical`), and the view's
point size lays it out. This sharpens every SVG on Windows above 100 %, not only the rows; icon sizes on the main page,
in the header and on the cards are unchanged.

With sharp glyphs the icons' stroke (`IconLineWidthScale`, new, 1.25 on Windows) matches Settings': about 300 inked
pixels per 20-point icon with the same mean brightness (209), where the soft icons had no pixel brighter than `#D9D9D9`.
