# Issue #318 — Menu buttons: a button or page action that opens a native menu

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/318
**Branch:** issue/318-menu-buttons
**Status:** Completed
**Stage:** 2 of the app-review plan (#332), last item

## Plan

### Gap
MAUI has `FlyoutBase.ContextFlyout` on Windows and Mac Catalyst and nothing on iOS or Android, and no way to hang a menu on a header-bar action. Spine owns the header bar, the `PageAction` model, the SVG icons and the glass button mapping, so a menu button is a model plus one handler mapping per platform.

### Design
- **Model** (`Core/Menu/`): `MenuElement` (observable base), `MenuAction` (Title, Svg, Command, CommandParameter, IsChecked, IsEnabled, IsDestructive, KeepsMenuOpen), `MenuSection` (optional Title, inline group with separators), `SubMenu` (Title, Svg, children), `MenuPicker` (single-selection group with checkmarks: Items, Selected, Command run with the picked action's CommandParameter or the action itself), `MenuItems` (the observable root collection). Shared with the context-menu idea (#306). The palette row waits for a design.
- **Attached properties** `MenuButton.Items` and `MenuButton.ShowsSelection` on `Button` and `ImageButton` (`MenuButton`, because MAUI already has an obsolete `Menu` class in the global XAML namespace). With `ShowsSelection` the button's `Text` follows the picked action.
- **`PageAction.Menu`** (+ `MenuShowsSelection`), a constructor without a command, `Command` nullable; `PageActionView` forwards the menu to its buttons, so a header action opens a menu with no app code.
- **Change tracking**: one `MenuObserver` watches the whole tree (collections and action properties) and rebuilds the native menu on any change, so `IsChecked`, `Title` and `IsEnabled` edits show at the next open.
- **Apple**: `UIButton.Menu` + `ShowsMenuAsPrimaryAction`; `ChangesSelectionAsPrimaryAction` for `ShowsSelection`; sections as inline `UIMenu`s, pickers with `SingleSelection`, submenus nested, `Destructive`/`Disabled` attributes, `KeepsMenuPresented` on iOS 16+; SVG icons rendered through `SvgBitmapLoader` as template images. The glass morph on iOS 26 comes from UIKit.
- **Android**: the button's click listener is replaced with one that shows a `PopupMenu` anchored to it, icons forced visible on API 29+, sections as groups with dividers, pickers as exclusive checkable groups, submenus, destructive titles tinted red, disabled items. The menu closes on every pick (no keeps-open on Android).
- **Windows**: `MenuFlyout` set as the button's `Flyout`: `MenuFlyoutItem`, `ToggleMenuFlyoutItem`, `RadioMenuFlyoutItem`, `MenuFlyoutSubItem`, `MenuFlyoutSeparator`, icons from the rendered PNG.
- A button with a menu has no `Command`: the menu is its action. Documented, and the header-bar action without a command is the natural shape.

### Steps
1. Model, `MenuButton` attached properties, `MenuObserver`, pick dispatch.
2. Apple mapping; Android mapping; Windows mapping.
3. `PageAction.Menu`, `PageActionView`.
4. Sample: `Pages/Menus/MenusPage` (a header "Filter" action with sections, a picker and a submenu; a glass "Sort" button with `ShowsSelection`; an icon button with toggles and a destructive item; a label with the last pick), index row.
5. Docs: `docs/wiki/menus.md`, README row, a line in `page-actions.md`, `/spine-page` and `/spine-controls` skills.
6. Build iOS, Android, Mac Catalyst; verify on the simulator and emulator; Windows via CI.

## Open Questions

None.

## Changes

- `Core/Menu/MenuElements.cs`: `MenuElement`, `MenuItems`, `MenuAction`, `MenuSection`, `SubMenu`, `MenuPicker` (observable, collection initializers). `Core/Menu/MenuObserver.cs`: watches a tree and reports any change.
- `Extensions/MenuButton.cs`: `Items` and `ShowsSelection` attached properties, the shared pick dispatch (`Pick`) and the icon renderer (`Icon`, PNG bytes through `SvgBitmapLoader`).
- Apple: `UIButton.Menu` built from the model (inline sections, single-selection pickers, submenus, destructive/disabled/keeps-open attributes, template images), `ShowsMenuAsPrimaryAction`, `ChangesSelectionAsPrimaryAction`; rebuilt on every model change; the pop-up chevron's width added to the button's trailing padding once. The duplicated `ConfigureGlassButtons()` call is gone.
- Android: a click listener showing a `PopupMenu` (icons on 29+, group dividers on 28+, exclusive checkable picker groups, submenus, red destructive titles, disabled rows), replacing MAUI's own listener so no command runs under the menu.
- Windows: `MenuFlyout` as the button's `Flyout` with `MenuFlyoutItem`, `ToggleMenuFlyoutItem`, `RadioMenuFlyoutItem`, `MenuFlyoutSubItem`, `MenuFlyoutSeparator`, `ImageIcon`s from the rendered PNG.
- `PageAction`: `Menu`, `MenuShowsSelection`, a constructor without a command, `Command` nullable; `PageActionView` forwards the menu and the pop-up flag to its buttons.
- Sample: `Pages/Menus/MenusPage` (header "more" action with a titled picker section, a submenu of toggles and a destructive row; a glass pop-up "Name" button; an icon button with plain actions; last pick and a summary), index row "Menu buttons".
- Docs: `docs/wiki/menus.md`, README row, a section in `page-actions.md`, `/spine-page` and `/spine-controls` skills.
- Verified on the iPhone 17 simulator (iOS 26): the header action opens a menu with icons, a checked picker, a separator and two submenus; picking moves the check and runs the command; the glass pop-up button shows "Name ◇" on one line and reads "Date ◇" after a pick. On the Pixel 10 Pro emulator: the popup shows the section title, radio picker, icons, dividers and submenus; toggles flip in the submenu; the pop-up button follows the pick. Mac Catalyst builds; Windows via CI.

## Decisions

- `MenuButton`, not `Menu`, for the attached properties: MAUI still ships an obsolete `Menu` class in the global XAML namespace, and `Menu.Items` would be ambiguous.
- A menu button has no command, on every platform. iOS never fires the tap for a button whose menu is the primary action, Android's listener is replaced, and Windows would run both; one rule is simpler than three.
- The whole native menu is rebuilt on any model change instead of diffing it. Menus are a handful of rows and the rebuild happens off the open menu, at the next tap.
- On iOS the pop-up chevron is reserved by adding its width to the MAUI `Padding` once (the frame grows) and subtracting it again from the content insets (the title keeps its room). MAUI measures a `Button` as text plus padding and knows nothing about the indicator; without this the title wrapped to two lines.
- Section titles are drawn on Android and Windows (a disabled header row on Windows) but not by UIKit in a button menu; the separator still is. Documented rather than emulated.
