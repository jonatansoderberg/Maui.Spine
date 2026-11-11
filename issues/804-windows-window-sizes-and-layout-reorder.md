# Issue #804 — Windows: window sizes from the content in the system menu, and reordering in a bindable layout

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/804
**Branch:** issue/windows-window-sizes
**Status:** In Progress

## Plan

1. The page's `SuggestWindowSizes` in the window's system menu on Windows, `FitWindowToContent()` and the fit at launch
   there too; one setting for both desktops (`options.Desktop.FitWindowToContent`).
2. The shared logic (which suggestion fits a width, whether to resize, which item is checked) in one place for both.
3. A Windows partial for `LayoutReorderState`: the layout's panel passes the pointer to the shared drag.

## Changes

- `WindowSizeMenu.Windows.cs` (new): on `WM_INITMENUPOPUP` for the window menu, the sizes are inserted above Close after
  a separator, the current one checked; `WM_SYSCOMMAND` with one of their ids resizes. `GetSystemMenu(hwnd, false)` once
  at attach gives the window its own menu (the shared one can't be changed). Resizing restores a maximised window and
  sets the whole window with `MoveAndResize` from the client size plus the frame Windows draws, keeping the visible
  window inside the monitor's work area (`GetMonitorInfo`'s `rcWork`) and its top left corner. Also `WindowFitter` and
  the fit at launch, as on the Mac.
- `WindowSize`: `ForWidth`, `Changes` and `Matches`, used by the Mac and Windows menus alike.
- `WindowMeasure` on Windows measures with the XAML root's size, the client area.
- `SpineOptions`: `Windows.FitWindowToContent` and `Desktop.FitWindowToContent` (`bool?`); `MacOS.FitWindowToContent` stays
  a `bool` (no binary break) and the desktop setting can turn it on too.
- `Reorder.Layout.Windows.cs` (new): pointer handlers on the layout's panel (handled events too). Mouse and pen: the
  press takes the item and it lifts after 4 points of movement, so a click stays a click; touch: a 500 ms hold lifts it,
  a move before that lets it go. The panel captures the pointer once the item is lifted.
- Docs: `windows-options.md` (Window sizes from the content), `macos-options.md`.

## Decisions

- **The system menu, not a menu of Spine's own.** A right-click on a Windows title row opens the window menu; it is
  where the sizes belong, and Alt+Space reaches them from the keyboard.
- **`MoveAndResize`, not `AppWindow.ResizeClient`.** With the content extended into the title bar, `ResizeClient` counts
  Windows' own title bar out of the client area and made every window that much taller (45 px at 150 %).
- **The monitor's work area from Win32.** `DisplayArea.WorkArea` gave the whole screen here, so a full-height window
  covered the taskbar.
- **The measure uses the client area.** MAUI's `Window` size on Windows is the outer frame with the invisible resize
  borders: the content was measured a few points too narrow, one column fewer in a masonry layout, and the root page is
  shorter by the title bar row MAUI keeps.
- **No placeholder frame on Windows yet.** The other items slide aside and leave the gap; the dashed frame the Mac draws
  would need an overlay of its own.

## Verified

- Windows, AIDash Connect at 150 %: the system menu has "One column", "2 columns", "3 columns" above Close; three and
  two columns fit the cards with the page margin below the last; one column fills the work area down to the taskbar and
  scrolls (confirmed by the user that the menu works).
- Not verified: dragging a card (the test pointer landed in another window), touch, the Mac build of the shared
  `WindowSize` helpers.
