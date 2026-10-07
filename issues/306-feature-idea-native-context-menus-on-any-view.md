# Issue #306 — Feature idea: Native context menus on any view

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/306
**Branch:** issue/306-feature-idea-native-context-menus-on-any-view
**Status:** Completed

## Plan

This plan follows the study `docs/proposals/spine-context-menus.md` and the decisions from 2026-09-30 recorded there:
- The shared model is `MenuItems` from #318, not `PageAction`.
- `IsVisible` is added to the model.
- The surface is an attached `ContextMenu.Items`.
- v1 lifts the view itself on iOS 16+ and has no custom preview.
- Android uses a `PopupMenu` anchored to the view.
- If a `Button` has both `MenuButton.Items` and `ContextMenu.Items`, the menu button wins.

This issue covers delivery steps 1–3 of the study. `DataGrid.RowContextMenu` (step 4) and `ContextMenu.Preview` (step 5) are out of scope; see Open Questions.

### 1. Share out the builders (no change in behaviour for menu buttons)
- `Core/Menu/MenuElements.cs`: `IsVisible` (default `true`) on `MenuAction` and `SubMenu`, observed like the other properties.
- `Extensions/MenuExtensions.Apple.cs`, `Platforms/Android/MenuExtensions.Android.cs`, `Platforms/Windows/MenuExtensions.Windows.cs`: all three builders skip invisible rows.
- Android: `Fill`/`AddAction`/the icon helper move out of `MenuClickListener` into a static `ShowPopup(anchor, items, owner, parameter)`. Both the menu button and the context menu call it.
- `Extensions/MenuButton.cs`: `Pick(owner, action, picker, parameter)` falls back to `parameter` when `action.CommandParameter` is null. The menu button passes `null`.
- Verified on the existing `Pages/Menus` page.

### 2. `ContextMenu.Items` and `ContextMenu.CommandParameter`
- New `Extensions/ContextMenu.cs` with the attached properties and a `ContextMenuState` that follows `HandlerChanging`/`HandlerChanged`, as `TapState` does (`Tap.cs`). Platform parts go in `ContextMenu.Apple.cs`, `Platforms/Android/ContextMenu.Android.cs` and `Platforms/Windows/ContextMenu.Windows.cs`.
- The native menu is built **when it opens**, not when the property is set. A recycled row then always shows the current item, and a row costs one interaction or listener, not a `MenuObserver`.
- **Apple:**
  - A `UIContextMenuInteraction` whose `GetConfigurationForMenu` returns `BuildMenu(...)`.
  - On iOS 16+, `GetHighlightPreview`/`GetDismissalPreview` lift the view with a `VisiblePath` rounded like its `Border` (the corner calculation is shared with `TapState.CornerRadius()`) and the view's background, or `SystemBackground` when it has none.
  - The tap highlight is cleared when the menu shows (`WillDisplayMenu` → `CancelPress`).
  - On a `Button`, `UIButton.Menu` is used with `ShowsMenuAsPrimaryAction = false` instead of an extra interaction. If `MenuButton.Items` is also set, it wins.
- **Android:**
  - The long-click listener and, on API 23+, the context-click listener open `ShowPopup`.
  - The long-click accessibility action is labelled from `SpineStrings` ("Actions"), so TalkBack says what a long press does.
- **Windows:** `UIElement.ContextFlyout`, filled in `MenuFlyout.Opening` with `FillFlyout`.

### 3. Sample and documentation
- New Showcase page `Pages/ContextMenus` (with an entry in `SampleIndex`) with:
  - a card that has `Tap.Command` and a menu;
  - a `CollectionView` with one shared row menu and the row's item as `CommandParameter`, including Follow/Unfollow through `IsVisible`;
  - the same `MenuItems` on the header bar as a page action.
- A "Context menus" section in `docs/wiki/menus.md` and a line in the `/spine-controls` skill.
- Verified on:
  - the iPhone 17 simulator: lift, rounded corners, tap and long press on the same card, rows in a list;
  - the Pixel emulator: long press, popup placement, TalkBack label;
  - Mac Catalyst: right click.
- Windows is checked by compiling only (the Windows TFM on the Mac, see memory). CI is paused.

### Untested points from the study, to settle on the way
- Whether `Tap.Command` runs when the finger is lifted after the menu has opened (Apple).
- Whether MAUI's gesture listeners on Android consume the long-click. If they do, a `GestureDetector` of our own is needed.
- Whether section titles are drawn in a context menu on Apple.
- Whether VoiceOver offers the menu automatically.

## Open Questions


## Changes

- **Shared model:** `MenuAction.IsVisible` and `SubMenu.IsVisible` (default `true`). All three builders skip hidden rows, in menu buttons too.
- **`MenuButton.Pick`** takes a fallback parameter. An action without a `CommandParameter` of its own gets it. The menu button passes `null`. The picker's rule is unchanged.
- **Android builder:** lifted out of `MenuClickListener` into a static `ShowPopup(handler, anchor, items, owner, parameter)`. The menu button and the context menu both call it. A destructive row's icon is now red like its title (`DestructiveColor`).
- **Apple and Windows builders:** the fallback parameter is threaded through (`BuildMenu`/`BuildChildren`/`BuildAction`, `FillFlyout`/`BuildItem`).
- **`Extensions/ContextMenu.cs`:** `ContextMenu.Items` and `ContextMenu.CommandParameter`, plus `ContextMenuState`, which follows the handler as `TapState` does. `CanOpen` requires an enabled view with at least one visible row.
- **`ContextMenu.Apple.cs`:**
  - A `UIContextMenuInteraction` whose `actionProvider` builds the menu at opening.
  - On iOS 16+, `GetHighlightPreview`/`GetDismissalPreview` lift the view with a rounded `VisiblePath` and its background (`SystemBackground` when it has none).
  - `WillDisplayMenu` calls `TapState.AbandonPress()`.
  - A `UIButton` gets `UIButton.Menu` with an uncached `UIDeferredMenuElement` and `ShowsMenuAsPrimaryAction = false` instead of a second interaction. `MenuButton.Items` wins and hands the menu back when cleared.
- **`ContextMenu.Android.cs`:** a long-click listener and, on API 23+, a context-click listener open `ShowPopup`. Returning `true` keeps a `Tap.Command` click from following. The long-click accessibility action is labelled `Spine.ContextMenu.Open` ("Show actions" / "Visa åtgärder").
- **`ContextMenu.Windows.cs`:** `UIElement.ContextFlyout` with a `MenuFlyout` filled in `Opening` and hidden when there is nothing to show.
- **`Tap`:** `TapState.CornerRadiusOf(view)` is shared with the preview. `TapState.AbandonPress()` on Apple fails the press recogniser and clears the highlight.
- **Showcase:** page "Context menus" (`Pages/ContextMenus`) with:
  - a card with `Tap.Command` and a menu that has Follow/Unfollow through `IsVisible`, shared with the header's ⋯ action, which takes the theme menu's slot;
  - a `CollectionView` with one shared `RowMenu` through `{PageBinding RowMenu}` and the row as `ContextMenu.CommandParameter`.
- **Docs:**
  - `docs/wiki/menus.md` has a "Context menus" section, context-menu platform notes and `IsVisible`.
  - README feature row.
  - `/spine-controls` line.
  - The study's status line.
  - Follow-up issue #466 for `DataGrid.RowContextMenu`.

### Verified
- **iPhone 17 Pro simulator (iOS 26.2):**
  - A long press lifts the card with its rounded corners and background and shows Follow / Add to calendar / Share, with Unfollow hidden. The tap command did not run after the long press.
  - Follow switches the rows, and the header's ⋯ menu then shows Unfollow (same instance, rebuilt by `MenuObserver`).
  - A plain tap still runs `Tap.Command`.
  - In the list, Remove removed the pressed row. After the removal, the recycled cell copied "Relay" (checked with `simctl pbpaste`).
- **Pixel 10 Pro emulator (API 36):**
  - A long press shows the `PopupMenu` at the card, or above a row when there is no room below.
  - Follow and Remove act on the right item.
  - The destructive icon is red.
  - The header's ⋯ menu button still opens through `ShowPopup`.
- **Mac Catalyst:** a right click shows the compact menu at the pointer, **with icons**, which answers the study's open point.
- **Jonatan's iPhone (iOS 27, Debug build against the 26.2 SDK):** checked by Jonatan, "funkar".
- **Windows:** compiles (`net10.0-windows10.0.19041.0` on the Mac); not run.
- **Not verified:**
  - TalkBack reading "Show actions".
  - Whether VoiceOver offers the menu.
  - A right click on Android with a mouse.

## Decisions

- **`DataGrid.RowContextMenu` is a follow-up issue, #466** (Jonatan, 2026-10-07). It carries the study's biggest uncertainty, its long-press conflict with copying, and the rest stands without it.
- **The Apple button case uses an uncached `UIDeferredMenuElement`.** A `UIButton` menu has to exist before the long press. A deferred element that asks on every opening keeps "built when it opens" there too (iOS 15+, Spine's minimum).
- **`AbandonPress` fails the tap recogniser** instead of only clearing the highlight. Otherwise lifting the finger from the open menu could still end the press as a tap.
- **The Showcase page takes the theme menu's header slot.** The header bar has one trailing slot, so the page removes the base class's theme action, as the Page actions page does, to show that a page action and a context menu share one `MenuItems`.
- **The Android destructive icon is tinted red.** Before, the title was red and the icon black, in menu buttons too. UIKit tints both.
