# Issue #794 — Windows title bar: every action, the caption line, the page title, binding context; a desktop-wide backdrop

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/794
**Branch:** issue/794-windows-title-bar-actions-title-backdrop
**Status:** Completed

## Plan

Found while bringing AIDash Connect (a Mac Catalyst dashboard on Spine 0.1.36) to Windows, where the app worked around
Spine for a Mac-like one-row header (its `Platforms/Windows/TitleBarLook.cs`).

1. Title bar: draw `MoreSecondaryPageActions` beside `SecondaryPageAction`, ordered as `HeaderBarView` does.
2. Keep the title bar at the caption buttons' 32 points, actions 46 × 32 (they already use the Fluent hover/press
   overlays of `ImageButtonVisualStates`).
3. Page title in the app's title style in the title bar (proposed `TitleBarShowsAppTitle`).
4. Inherited binding context for `TitleBar.LeadingContent` / `Content` / `TrailingContent`.
5. `options.Desktop.Backdrop` for both desktops; the platform options become overrides.
6. `SpineRow`'s status mark clipped on Windows.
7. A window title for the taskbar and Alt+Tab.

## Changes

- `SpineApplication.Windows.cs`: the title bar's trailing content is a row: the further trailing actions innermost
  first, then the secondary action at the edge (`SyncMoreTitleBarActions`), re-synced on `MoreSecondaryPageActions`.
- The title bar has `HeightRequest`/`MinimumHeightRequest` 32 (`CaptionHeight`), each action 46 × 32 (`CaptionButtonWidth`).
- `InheritTitleBarBindingContext`: the title bar's content gets the root region as inherited binding context, on every
  change of the content or the context.
- The MAUI window gets `AppTitle` as its title when the app has not set one.
- `SpineOptions.Desktop.Backdrop` (new `DesktopOptions`); `Windows.Backdrop` and `MacOS.Backdrop` are now
  `WindowBackdrop?`, null by default; `SpineOptions.WindowsBackdrop` / `MacBackdrop` resolve platform → desktop →
  the platform's old default (Mica / None), so nothing changes for an app that sets neither.
- `SpineRow`: the status glyph is the mark's own size with no SVG padding, instead of 1.5 × the mark.
- Docs: `windows-options.md` (backdrop, title bar, the one-row header bar), `macos-options.md` (backdrop).

## Decisions

- **No `TitleBarShowsAppTitle` option (item 3 dropped).** The Showcase already shows the one-row look on Windows: with
  `RegionDefaults.IsTitleBarVisible = false` and `IsHeaderBarVisible = true` the header bar moves into the caption
  row, with the app's `HeaderBarTitle` style and every action, and its buttons take clicks through the passthrough
  regions Spine keeps (`SpineApplication.NonClient.Windows.cs`). A second way to get the same row would be API to keep
  in step; the docs now say that this is the way to match a Mac app's `UnifiedTitleBar`.
- **No re-measure workaround.** In a 500-point window, title + subtitle + three actions + the caption buttons are wider
  than the window, and MAUI's `TitleBar` gives its auto columns their full width, so the row runs under the caption
  buttons. That is a lack of room, not a stale measure (the row measured right: 46 + 55 + 46); in a wide window the
  actions sit together before the caption buttons. Left as it is.
- **The badge glyph:** the SVG canvas pads by 5 px, so a 1.5 × 22 = 33-point glyph drew in about 23 points; at 22 points
  without padding it draws the same. Platform-neutral, so iOS, Mac and Android keep their look.

## Verified

- Windows, the Showcase built from this branch (the title bar shown through a local, uncommitted
  `RegionDefaults.IsTitleBarVisible = true`): Page actions shows back, the title and subtitle, the bell and Filter, and
  the star when shown, on the caption buttons' line; the window is named "Spine Showcase" for the taskbar. The
  Showcase's own setup (header bar in the caption row) unchanged.
- Not verified on screen: the status mark on the Rows page (the run was cut short), the inherited binding context, and
  the Mac (the backdrop change there is the property the code reads).
