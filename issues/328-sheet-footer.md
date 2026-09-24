# Issue #328 — Sheet footer pinned to the visible detent, and content inset for the sheet's own buttons

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/328
**Branch:** issue/328-sheet-footer
**Status:** Completed

## Plan

1. Spike: find out whether a footer can follow an interactive drag between Medium and Large on
   iOS and Android, not just the snap points.
2. `SpinePage.Footer`, rendered by the region's page presenter outside the scrolling content.
3. Check the automatic top inset for the sheet's close/header buttons (`HeaderBarConstants.SheetTopPadding`).
4. Docs (`docs/wiki/sheets.md`, `/spine-page` skill) and samples: the main sample's Bottom sheets
   page, and the push sample's tags sheet opening at Medium.

## Open Questions

- iOS 15–18 could not be run (only iOS 26 simulator runtimes here). If an older UIKit slides the
  sheet instead of resizing it, the footer would sit at the full-height bottom there; the Android
  overhang path would be the model for a fix.
- Orientera's sheets (`Padding="16,36,16,16"`, bottom button stacks) are for its own repo to update
  when it takes this Spine version: drop the 36, move Save/Cancel to page actions, and a
  Continue-style primary action into `Footer`.

## Spike findings

**iOS (26.2/26.4 simulator, iPhone 17).** `UISheetPresentationController` resizes the presented
view controller's view on every frame of an interactive drag, not only at the detents. Logged from a
`CADisplayLink` while dragging Medium → Large: the view's height went 423 → 433 → 442.7 → … → 770 →
812 in ~10 pt steps, one per frame, following the finger. The snap to the detent at the end of the
drag is a UIKit animation of the same resize. The MAUI region is pinned to that view with Auto
Layout, so MAUI lays the page out against the visible height on every frame by itself: a footer in
an `Auto` row under the content is at the bottom of the visible sheet at every detent and during the
drag, with no callback at all. At Medium on iOS 26 the card also floats (inset 8 pt, scaled to
386 pt wide), and its view still reports a 34 pt bottom safe-area inset, so the existing bottom
padding is right. No iOS code was needed; the display-link probe was removed again.
`sheetPresentationControllerDidChangeSelectedDetentIdentifier` was not needed either. Older iOS
versions could not be run here (only iOS 26 runtimes are installed), so iOS 15–18 are unverified.

**Android (Pixel 10 Pro emulator).** `BottomSheetDialog`'s sheet (`design_bottom_sheet`) is
`MATCH_PARENT`, laid out at full height, and `BottomSheetBehavior` slides it down to each detent with
`offsetTopAndBottom` — no layout pass, so MAUI never sees the detent. The visible height has to be
computed: the part of the sheet below the screen is `sheet.Top + sheet.Height - parent.Height`.
`BottomSheetCallback.OnSlide` fires on every frame of a drag and of the settle animation, so the
footer follows the finger. The first position comes from a layout pass instead of a slide, and the
behaviour offsets the sheet to its detent *after* the layout that raises `LayoutChange`, so that
report is posted. Below the smallest detent (dismissing) the value is clamped so the footer leaves
with the sheet.

**Windows.** The WinUI sheet host animates `sheet.Height` itself (drag and snap), so the MAUI region
is resized like on iOS and the footer follows with no extra code. Not run (CI compile only).

**Mac Catalyst.** Same UIKit path as iOS; a page sheet there has no detents. Built, not run.

Interactive following works on every platform, so no snap-point fallback was needed.

## Changes

- `SpinePage.Footer` (bindable `View?`): a view pinned to the bottom of the page, outside its
  scrolling content, that gets the page's `BindingContext`.
- `PagePresenter`: a third `Auto` row hosts the footer; the content row ends above it. It also takes
  a sheet *overhang* (how far the page reaches below the visible edge of the sheet): the content gets
  that much bottom margin and the footer is lifted by it.
- `NavigationRegion.SetSheetOverhang`, fed by the Android sheet presenter from `OnSlide` and the
  first layout; reset to zero on dismiss.
- Android sheets no longer pad their content for the navigation bar a second time: the sheet's
  wrapper view already does (and drops it while the keyboard is up). The double inset left
  24 dp of empty sheet under the content, most visible under a footer.
- Main sample, Bottom sheets page: a "Buttons in a sheet" section with two new sheets and code
  examples — `EditSheetPage` (Save and Cancel as page actions) and `LoginSheetPage` (Log in in the
  footer, fields in a scroll view). The page itself now scrolls.
- Push sample, tags sheet: opens at Medium (the forced `InitialDetent = FullScreen` and its comment
  are gone), Save is a page action in the header bar, the rest scrolls.
- Docs: `docs/wiki/sheets.md` (footer, where buttons go, the top inset); `/spine-page` skill.

## Decisions

- **Button stacks in sheets are page actions, per Jonatan 2026-09-24.** Save, Cancel, Done — confirm
  and dismiss — always go in the sheet's header bar as `[PageAction]`, never in a button stack at
  the bottom. The footer is for a sheet's own primary action (Log in, Continue, Pay). The push
  sample's Save is therefore a page action, not a footer button.
- **Content is laid out against the visible sheet on Android for every sheet page, not only pages
  with a footer.** iOS already does this (UIKit resizes the view), and without it a scrolling page
  could not reach its end at Medium on Android. Per frame during a drag, as on iOS.
- **The top inset for the sheet's own buttons already exists; nothing new was added.** Since
  81c5ba2 (in v0.1.0) the sheet region pads the page by `HeaderBarConstants.SheetTopPadding` and the
  page presenter reserves a title row the height of the header bar, so the first line of content
  starts under the close button on both platforms (verified on the sample sheets, which use a plain
  `Padding="24"` and `Padding="20,0,20,16"`). Pages must not add their own ~36 pt top padding for the
  buttons; anything they add is spacing of their own. Documented in `sheets.md`. No sample sheet had
  a hand-picked top constant, so none gets double padding.
- **No iOS detent tracking.** The spike showed UIKit resizing the view per frame, so a display link
  or detent delegate would only duplicate what layout already does.
- The footer is not given a background: at rest the content ends above it, so nothing scrolls
  behind it.
- Other sample sheets were checked for bottom button stacks: none. `FullscreenSheetPage` has three
  centred buttons that demonstrate the three result outcomes (result, null, cancel) and stays as is;
  `SmallSheetPage` has one Close button in a compact sheet.
