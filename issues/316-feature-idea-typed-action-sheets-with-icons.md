# Issue #316 — Feature idea: Typed action sheets with icons

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/316
**Branch:** issue/316-feature-idea-typed-action-sheets-with-icons
**Status:** Completed

## Plan

The rolling-numbers half of the issue shipped in #432 (`AnimatedLabel Mode="RollingNumber"`). This
issue now covers the typed action sheet only. There is no study in `docs/proposals/` for it; the
decisions below were made while Jonatan was away and are listed for review.

### API
- `INavigationService.ShowActionsAsync(ActionSheet sheet, View? anchor = null)` returns
  `Task<MenuAction?>`: the picked action, or `null` when the sheet was cancelled.
- `ActionSheet` (in `Core/Menu/ActionSheet.cs`, next to the menu model): `Title`, `Message`,
  `Actions` (`IList<MenuAction>`), `CancelText` (null = the localised `Spine.Header.Cancel`) and
  `CommandParameter` (the fallback parameter for actions without their own, as
  `ContextMenu.CommandParameter` is for a shared row menu).
- The rows are the existing `MenuAction` from #318/#306, so `new("Share", SpineIcons.Share, ShareCommand)`
  from the issue's sketch compiles as written, and a row can be shared with a menu. `Title`, `Svg`,
  `Command`, `CommandParameter`, `IsDestructive`, `IsEnabled` and `IsVisible` apply; `IsChecked` and
  `KeepsMenuOpen` are menu-only. A sheet is flat, so sections, submenus and pickers are not taken.
- The picked row's command runs (with `CommandParameter ?? sheet.CommandParameter`) and the task
  completes with the row, so both the command style and the `await` style work.

### Platforms
- **iOS / iPadOS / Mac Catalyst** (`Presentation/ActionSheetPresenter.Apple.cs`): `UIAlertController`
  with `PreferredStyle = ActionSheet`, presented from the topmost view controller. Icons as template
  images through the `UIAlertAction` `image` key (KVC). Destructive rows use
  `UIAlertActionStyle.Destructive`, a Cancel row `UIAlertActionStyle.Cancel`. On iPad the
  sheet is a popover: from `anchor` when given, otherwise centred with no arrow (as MAUI's
  `DisplayActionSheet` does). A dismissal without a pick (tap outside the popover, Esc) completes
  with `null`.
- **Android** (`Presentation/ActionSheetPresenter.Android.cs`): a Material `BottomSheetDialog` with
  the M3 drag handle (`BottomSheetDragHandleView`), an optional title and message, and one 56 dp
  list row per action (24 dp icon, `bodyLarge` text, ripple). Destructive rows in `colorError`,
  disabled rows dimmed. No Cancel row: Material dismisses with a swipe, the scrim or Back.
- **Windows** (`Presentation/ActionSheetPresenter.Windows.cs`): a `MenuFlyout`, shown at the anchor
  or the centre of the window, the title as a disabled first item. Light dismiss is cancel.

### Screen readers
- Apple: `UIAlertController` is accessible as is; VoiceOver reads the title, message and buttons.
- Android: the dialog's title is the window title TalkBack announces; the header is marked as a
  heading; every row is a button with its title as its name.

### Sample, docs, tests
- Showcase page `Pages/ActionSheets` with an entry in `SampleIndex`: a sheet with icons and a
  destructive row, the awaited result shown on the page, a confirmation sheet, and an anchored
  sheet (popover on iPad).
- `docs/wiki/menus.md`: an "Action sheets" section; README row; `spine-page` skill line.
- `tests/Plugin.Maui.Spine.Core.Tests`: the pick logic (parameter fallback, `CanExecute`, visible
  rows) compiled in with `MenuElements.cs` and `ActionSheet.cs`, which have no MAUI types.

## Open Questions

None blocking; see Decisions.

## Changes

- `Core/Menu/ActionSheet.cs`: the model (`Title`, `Message`, `Actions`, `CancelText`, `CommandParameter`) with the shared pick logic. No MAUI types, so the tests compile it in.
- `Core/INavigationService.cs`, `Services/NavigationService.cs`: `ShowActionsAsync(ActionSheet, View? anchor = null)` returning `Task<MenuAction?>`; a sheet with no visible row returns `null` without showing anything.
- `Presentation/ActionSheetPresenter.cs` with `.Apple.cs`, `.Android.cs` and `.Windows.cs`.
  - Apple: `UIAlertController` action sheet, icons through the `image` key, Light/Dark from `UserAppTheme`, a popover source from the anchor (or centred on iPad), and a hidden watcher view that completes with `null` when the sheet leaves without a pick.
  - Android: Material `BottomSheetDialog` with `BottomSheetDragHandleView`, a header, 56 dp rows in a `NestedScrollView`, theme colours, TalkBack roles.
  - Windows: `MenuFlyout` at the anchor or the centre of the window.
- `Extensions/MenuButton.cs`: `Icon(IServiceProvider, …)` overload so the presenter can render icons without a handler. `MenuExtensions.Windows.cs`: the same for `BuildIcon`. `BottomSheetPageExtensions.Android.cs`: `ResolveMaterialColor` is internal.
- `tests/Plugin.Maui.Spine.Core.Tests/ActionSheetTests.cs`: the issue's sketch compiles as written, hidden rows, the parameter fallback, `CanExecute`.
- Showcase page `Pages/ActionSheets` ("Action sheets" in `SampleIndex`): actions with icons and a Cancel status, a destructive confirmation compared with `==`, and rows with a ⋯ button that anchors the sheet.
- `docs/wiki/menus.md`: an "Action sheets" section, a platform table and `images/action-sheet-ios.png` / `images/action-sheet-android.png`; README row; `spine-page` skill section.

## Decisions

- **Rows are `MenuAction`.** #317 asks for one menu model. `MenuAction` already has a title, an SVG, a command and `IsDestructive`, and its `(title, svg, command)` constructor makes the issue's `new("Share", Icons.Share, ShareCommand)` compile unchanged. `IsChecked` and `KeepsMenuOpen` are ignored; sections, submenus and pickers are not accepted (`IList<MenuAction>`), because no platform's action sheet has them.
- **The result is the row (`Task<MenuAction?>`), and its command also runs.** Both styles from one call: commands for shared rows, `picked == delete` for a confirmation. `null` is cancel. The command runs before the task completes.
- **`ActionSheet.CommandParameter`** mirrors `ContextMenu.CommandParameter`: one set of rows for every list item.
- **The anchor is a parameter, not a property,** so `ActionSheet` stays free of MAUI types (and testable on plain `net10.0`), and the view model passes the view only where it has one. From XAML: `CommandParameter="{Binding Source={RelativeSource Self}}"`.
- **iOS 26 placement.** Without an anchor, the iPhone shows the system sheet as is (on iOS 26 it floats in the middle, with a Cancel row). With an anchor, the source is set on the iPhone too, so iOS 26 grows the sheet out of the button (no Cancel row; a tap outside cancels). iPad needs a source: the anchor, or the centre of the window with no arrow, as MAUI's `DisplayActionSheet` does. On Mac Catalyst (the Showcase runs with the iPad idiom) the alert has no popover controller, so the anchor is unused there.
- **Icons on `UIAlertAction` use the `image` key (KVC).** It is not public API but is widely used and what the issue asked for. A refusal is caught and logged, and the row shows without an icon.
- **Cancellation on Apple is watched with a hidden subview** in the alert's view: when it leaves the window without a pick, the task completes with `null` one run-loop turn later. Measured order on the simulator: view leaves the window, then the row's handler, then the posted check, so a pick is never lost. This also covers the sheet being dismissed with its presenter (e.g. a Spine sheet closed from code), which would otherwise leave the task, and an `IAsyncRelayCommand` awaiting it, pending forever. `UIAlertController` is not subclassed (Apple does not support that).
- **Dark mode on Apple:** the alert's `OverrideUserInterfaceStyle` follows `Application.UserAppTheme` (the app's forced Light/Dark); the MAUI window does not pass it on. Unspecified follows the system.
- **Android has no Cancel row.** Material bottom sheets dismiss with a swipe, the scrim or Back; a Cancel row is an iOS idiom. The anchor is ignored on Android (always a bottom sheet). The drag handle is Material's `BottomSheetDragHandleView`, which TalkBack announces and can act on.
- **Windows uses a `MenuFlyout`**, consistent with menu buttons and context menus, rather than a `ContentDialog`. The title and message are dimmed rows above a separator.
- **Cancel text** defaults to the existing `Spine.Header.Cancel` string, so no new localisation key.
- **A sheet with no visible row** returns `null` without showing anything, as a context menu with no visible row opens nothing.

## Verification

- **iPhone 17 Pro simulator (iOS 26.4)**, driven by an injected harness (picks go through `UIAlertController`'s own dismiss-with-action path, as a tap does): icons and the red destructive row, a pick runs the command and returns the row, Cancel and a dismissal from code return `null`, the anchored row sheet grows out of its ⋯ button and passes the runner through `CommandParameter`, a sheet with no visible row returns `null`, Light and Dark.
- **Android emulator (emulator-5556, tablet-sized screen)**: the Material sheet with drag handle, header, icons, red destructive row and a dimmed disabled row; hidden rows left out; a tap picks (command ran), Back and the scrim cancel (`null`), Light and Dark. `uiautomator` shows the window titled with the sheet's title and each row as `android.widget.Button`.
- **Mac Catalyst** (binary run directly with a stdin harness): the alert is presented with the icons and Cancel; a pick runs the command; `PopoverPresentationController` is nil there, so the anchor is unused. The alert could not be captured on screen (macOS draws it outside the app's UIKit windows and the display was asleep).
- **Windows**: compiles only.
- `Plugin.Maui.Spine.Core.Tests`: 35 passed.
