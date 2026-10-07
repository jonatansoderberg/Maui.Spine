# Issue #466 — DataGrid: RowContextMenu, with the long-press copy moved into the menu

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/466
**Branch:** issue/466-datagrid-rowcontextmenu-with-the-long-press-copy-m
**Status:** Completed

## Plan

This is delivery step 4 of `docs/proposals/spine-context-menus.md` (§7), building on #306's `ContextMenu.Items`. Decided on 2026-09-30:
- With `RowContextMenu` set, a long press opens the menu with the row's item as the fallback parameter.
- Copying becomes a row at the top of the menu, "Copy *column*", for the cell under the finger.
- Without `RowContextMenu`, nothing changes.
- Swipe actions stay as they are.

### Approach: the grid puts the public `ContextMenu.Items` on each row
`Plugin.Maui.Spine.Controls.DataGrid` already references `Plugin.Maui.Spine`, so a row gets the same context menu as any other view and needs no new core API:
- **iOS:** the `UIContextMenuInteraction` with the lift.
- **Mac Catalyst:** the compact menu on right click.
- **Android:** the long-click and context-click `PopupMenu`.
- **Windows:** `ContextFlyout`.

The rejected alternative opens the menu from the grid's own long-press timer (`OnRowLongPress`). That needs a new public "show a menu at this view" API in core for Android and Windows, and iOS has no public way to open a context menu from code, so Apple would need the interaction anyway. The per-row attachment gives one path on every platform.

**Fallback if Android fails:** the row's MAUI `PointerGestureRecognizer`, or the `SwipeView` and `SwipeGate` around it, might swallow the long-click before the listener sees it. The study flagged this as untested. If it happens, Android opens the menu from `OnRowLongPress` through a small internal-to-public hook in core. Windows is unaffected.

### Changes
1. **`DataGrid.cs`:** a bindable `RowContextMenu` (`MenuItems?`). Changing it re-renders rows, like the swipe actions do.
2. **`DataGrid.Press.cs` / new `DataGrid.ContextMenu.cs`:**
   - The grid owns one composed `MenuItems`: a `MenuSection` with a "Copy *column*" `MenuAction` (`copy.svg`), followed by the elements of `RowContextMenu`. The app's elements are the same instances, so their own changes still show. The composed list is rebuilt when `RowContextMenu` or its collection changes.
   - `BeginRowPress` already finds the cell under the finger. It now also sets the copy row: its title from `Spine.DataGrid.CopyColumn` ("Copy {0}" / "Kopiera {0}"), and `IsVisible` only for a text cell with text when `IsCellCopyEnabled` is on.
   - The copy command copies that label's text through the existing `CopyCellTextAsync` (clipboard plus the "Copied" bubble or `CellCopied`). It checks first that the row still shows the same item.
   - `OnRowLongPress` no longer copies by itself when a menu is set. It still marks the press as long, so the release does not tap.
3. **`DataGrid.Rendering.cs` (`BuildRow`):** when the grid has a menu, the row `Grid` gets `ContextMenu.Items` = the composed menu and `ContextMenu.CommandParameter` bound to the row's `BindingContext` (the item). The menu goes on the row inside the `SwipeView`, so swiping is untouched.
4. **Right click on Windows and Mac:** the row's `PointerGestureRecognizer` keeps `Buttons = Primary` (the default), so a right click opens the menu and neither taps nor copies. To be checked in the code.
5. **Sample:**
   - The Showcase DataGrid page gets a row menu (for example Open, Duplicate, Delete) with a "Row menu" switch in its options sheet.
   - `LastAction` shows what ran on which product.
   - The generated code example is updated too.
6. **Docs:**
   - `docs/wiki/data-grid.md`: a "Row context menu" section, and the copying section rewritten for when a menu is set.
   - `docs/wiki/menus.md`: a pointer.
   - The DataGrid README.
   - The study's status line.
   - The `/spine-controls` line.

### Verification
- **iPhone 17 Pro simulator:**
  - A long press on a text cell lifts the row and shows "Copy Product name" plus the app's rows.
  - A long press on the checkbox or link column shows no copy row.
  - A tap still runs `RowTappedCommand`, the link and the checkbox.
  - Swipe actions still swipe.
  - Copy writes the clipboard.
- **Pixel 10 Pro emulator:** the same, plus that the long-click reaches the row through the swipe gate.
- **Mac Catalyst:** a right click on a row.
- **Windows:** compiles only.

## Open Questions

## Changes

- **`DataGrid.RowContextMenu`** (`MenuItems?`). Switching between null and a menu rebuilds the rows. Changing its collection recomposes the menu.
- **`DataGrid.ContextMenu.cs`:**
  - The grid composes one `MenuItems`: a section with the "Copy *column*" row, then the app's elements (same instances).
  - `AttachRowMenu` sets `ContextMenu.Items` and binds `ContextMenu.CommandParameter` to the row's item.
  - `TargetCopyRow` points the copy row at a text cell that shows text, or hides it.
  - `CopyFromMenu` copies through the existing `CopyCellTextAsync` after checking that the row still shows the same item.
- **`DataGrid.Press.cs`:**
  - With a menu, the row's recognizer takes `Primary | Secondary`. A secondary press only targets the copy row (and on Android opens the menu).
  - `PointerMoved` without a pending press targets the copy row for the cell under a hovering mouse.
  - `OnRowLongPress` marks the press as long and, instead of copying, opens the menu on Android.
- **Core, `ContextMenu.Show(view)`:** opens a view's context menu from code. Android uses `Open()` plus the long-press haptic, Windows `ContextFlyout.ShowAt`, and Apple returns `false` because UIKit has no public way to open a context menu from code.
- **Core fix from #306:** `ShowPopup`'s XML doc comment had ended up on `DestructiveColor` (CS1734 warnings).
- **Strings:** `Spine.DataGrid.CopyColumn`, "Copy {0}" / "Kopiera {0}".
- **Showcase DataGrid page:**
  - a row menu (Details, Star, Delete) with a "Row menu" switch in its options;
  - an updated hint text;
  - the code example.
- **Docs:**
  - `docs/wiki/data-grid.md`: a "Row context menu" section, and the copy section updated.
  - `docs/wiki/menus.md`: `ContextMenu.Show` and a DataGrid pointer.
  - The DataGrid README and the repo README.
  - The study's status line.
  - The `/spine-controls` line.

### Verified
- **iPhone 17 Pro simulator:**
  - A long press on "Compact clock" lifts the row and shows "Copy Product name", Details, Star and Delete. Copy put "Compact clock" on the clipboard (`simctl pbpaste`), and no row tap followed.
  - A long press on the Starred checkbox shows no copy row. Star starred that row.
  - A plain tap runs `RowTappedCommand`, and a swipe shows Delete.
- **Pixel 10 Pro emulator:**
  - At first the row's long click never fired (see Decisions). After the fix, a long press shows the `PopupMenu` with "Copy Product name".
  - Delete removed "Compact clock".
  - A plain tap still gives "Row: …", and a swipe still shows Delete.
- **Mac Catalyst:**
  - A right click shows the compact menu.
  - Before hover targeting there was no copy row, since a right click raised no `PointerPressed`. After it, the menu shows "Copy SKU" for the line under the pointer.
- **Jonatan's iPhone (iOS 27):** checked by Jonatan, "funkar".
- **Windows:** compiles (DataGrid and core, `net10.0-windows10.0.19041.0` on the Mac); not run.

## Decisions

- **The menu goes on each row through `ContextMenu.Items`.** This is the path for iOS, Mac and Windows. iOS cannot open a context menu from code anyway.
- **Android opens the menu from the grid's long press through a new `ContextMenu.Show`.** This was the planned fallback, and it was needed. The row's MAUI `PointerGestureRecognizer` consumes the touch, so the view's long-click listener never fires. `Show` is public because any view with MAUI gesture recognizers has the same problem on Android. `Show` adds the long-press haptic, since `performLongClick`, which normally gives it, never runs.
- **On a mouse, the copy row follows the hover.** On Mac Catalyst a right click raises no `PointerPressed` through MAUI, so a press cannot say which cell the menu is for. The grid targets the cell under the pointer on every `PointerMoved` when no press is pending. The title is only rewritten when the cell or item changes.
