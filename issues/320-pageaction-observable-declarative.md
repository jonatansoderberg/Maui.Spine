# Issue #320 — PageAction: observable properties and declarative creation

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/320
**Branch:** issue/320-pageaction-observable-declarative
**Status:** Completed
**Stage:** 1 of the app-review plan (#331)

## Plan

### Gap
`PageAction` is immutable (`Text`, `Command` get-only; `Svg`, `Placement`, `IsVisible` are `init`). The header reads `PageActions` only when `NavigationRegionViewModel` raises `PrimaryPageAction` / `SecondaryPageAction`, which happens on navigation events, not when the collection or an action changes. `ViewModelBase` listens to `PageActions.CollectionChanged` but only re-raises its own `DefaultPageAction`, which nothing in the header binds to. Consequences: the `Count == 0` guard in eight view models (recommended by `ViewModelBase`'s own docs and `page-actions.md`), Orientera's dropped "Filter (3)" label, and Almanacka's "Today" button that cannot be shown or hidden.

### Part A — observable `PageAction`
1. `PageAction : ObservableObject` (CommunityToolkit.Mvvm is already a dependency). `Text`, `Svg`, `IsVisible`, `CommandParameter` become settable with change notification; new `IsEnabled` (default `true`) and `Badge` (`string?`, `null` = none; a count or a short word). `Command`, `AsyncCommand` and `Placement` stay as they are. Constructors unchanged, so existing code compiles.
2. `ViewModelBase`: on `PageActions.CollectionChanged`, subscribe to / unsubscribe from each action's `PropertyChanged` (including `Reset`), and raise a new internal `PageActionsChanged` event on both collection changes and action property changes. Keep `DefaultPageAction` as is.
3. `NavigationRegionViewModel`: override `OnPropertyChanged`; when `CurrentRegionViewModel` changes, move the `PageActionsChanged` subscription to the new view model, and on that event raise `PrimaryPageAction` and `SecondaryPageAction`. That covers the case where a visibility change makes a different action (or the implicit back/close) win the slot.
4. `PageActionView`: subscribe to the assigned action's `PropertyChanged`. `Text`, `Svg`, `Badge`, `CommandParameter` → re-apply (animated only when `Svg` changes, as today); `IsEnabled` → both buttons' `IsEnabled`; `IsVisible` → re-apply and raise an internal `VisibilityChanged` event that `HeaderBar` uses to run `UpdatePrimaryActionVisibility` / `UpdateSecondaryActionVisibility`. The Windows title bar (`SpineApplication.Windows.cs`) hosts the same `PageActionView`s, so it gets the same behaviour for free.
5. Badge rendering in `PageActionView`: a small pill (`Border` + `Label`, theme accent background, white 11-pt text, min 16×16) anchored top-trailing over the icon or text button, visible only when `Badge` is non-null. Slot width re-measure already keys off `SizeChanged`, so a longer text or a badge keeps the title clear of the action.

### Part B — declarative creation
6. `PageActionAttribute` (`AttributeTargets.Method | Property`): `Text` (positional, optional), `Svg`, `Placement`, `Order`, `Badge`; on a `[RelayCommand]` method the generated `<Name>Command` property is resolved by the toolkit's rule (`SaveAsync` → `SaveCommand`, `Save` → `SaveCommand`); on a property of type `ICommand` the property itself is the command. `Command` (string) overrides the name when needed.
7. `PageActionDiscovery` (internal, `Services/`): reflects once per view-model type into a cached template list (`ConcurrentDictionary<Type, …>`), sorted by `Order`; `Populate(ViewModelBase)` creates the `PageAction`s and adds them, guarded by an internal `_declaredActionsAdded` flag on the view model so singleton pages and repeated `NavigableMeta.Apply` calls never duplicate. Called from `NavigableMeta.Apply`, which already runs before the page appears for regions, sheets and tab roots.
8. Hand-added actions keep working and land after the declared ones.

### Docs and sample
9. `page-actions.md`: attribute form first, runtime updates (`Text`, `Badge`, `IsVisible`, `IsEnabled`) instead of "clear and repopulate", property table updated; remove the `Count == 0` advice from `ViewModelBase` XML docs, `page-actions.md`, `regions.md` if present, and the `/spine-page` skill.
10. Sample: new `Pages/PageActions/PageActionsPage` from the start page: two attribute-declared actions (icon + text), buttons that toggle `IsVisible`, bump a counter into `Text` ("Filter (3)"), set a `Badge`, and flip `IsEnabled`. Existing sample view models (`MainPage`, `MainPageOld`, `SamplePage`, `SamplePage2`, `GlassPage`) move to the attribute and lose the guard.
11. Build iOS, Android, Mac Catalyst; verify on the iPhone 17 simulator and the Pixel emulator that changes apply without navigation; check the Windows title-bar path compiles.

## Open Questions

None blocking. Decided rather than asked:
- `Badge` is a `string?` rather than an `int?`, so it can show "3", "•" or "NEW"; the pill is the same either way.
- The attribute goes on the `[RelayCommand]` method (as the issue sketches) and also on an `ICommand` property, so hand-written commands work without a generated name.

## Changes

- `Core/PageAction.cs`: now `ObservableObject`; `Text`, `Svg`, `CommandParameter`, `IsVisible` settable with notification; new `IsEnabled` and `Badge`. Constructors, `Command`, `AsyncCommand` and `Placement` unchanged.
- `Core/PageActionAttribute.cs` (new): `[PageAction]` on a `[RelayCommand]` method or an `ICommand` property; `Text`, `Svg`, `Placement`, `Order`, `Badge`, `IsVisible`, `Command`.
- `Services/PageActionDiscovery.cs` (new): reflects once per view-model type (cached), resolves the command property by the toolkit's naming rule, adds the actions once per instance (`ViewModelBase.DeclaredActionsAdded`); called from `NavigableMeta.Apply`, so regions, sheets and tab roots all get it before first appearance. A declaration that points at no command property throws with the expected name in the message.
- `Core/ViewModelBase.cs`: watches every action in `PageActions` (reconciled on each collection change, including `Reset`) and raises an internal `PageActionsChanged`; XML docs no longer recommend the `Count == 0` guard.
- `Presentation/NavigationRegionViewModel.cs`: follows the front page's `PageActionsChanged` (re-wired whenever `CurrentRegionViewModel` or `PrimaryPageAction` is raised) and re-raises `PrimaryPageAction` / `SecondaryPageAction`.
- `Presentation/PageActionView.cs`: subscribes to its action; `Svg` change cross-fades, others re-apply in place; applies `IsEnabled`; renders `Badge` as a red pill (same colour as the native tab badges); raises `VisibilityChanged`, which `HeaderBar` uses to re-run slot visibility for an instance that hid or showed itself. The Windows title bar hosts the same views and needs no change.
- Sample: new `Pages/PageActions/PageActionsPage` (two declared actions, buttons for count/badge, enabled, visibility, and adding/removing a Primary "Cancel"); `MainPage`, `MainPageOld`, `SamplePage`, `SamplePage2` and `GlassPage` use the attribute and lose their `OnAppearingAsync` guards.
- Docs: `page-actions.md` rewritten around the attribute and runtime changes; `page-pattern.md` and the `/spine-page` skill updated.

## Decisions

- Change notification flows through the view model (`PageActionsChanged`) rather than having `HeaderBar` watch the collection itself: the region view model already owns the resolution rules (explicit vs implicit back/close), and the Windows title bar reads the same two resolved properties.
- `PageActionView` also watches its own action so a change that does not alter which action wins the slot (text, badge, enabled) updates in place without a fade.
- Declared actions are resolved by reflection at runtime (cached per type) rather than a source generator, matching how `[NavigableRegion]` is discovered today; the command property lookup uses `Instance | Public | NonPublic`, which is what a trimmed app keeps for members the generated code references anyway.
- `IsEnabled` also dims the button to 40 % opacity: the app's `Disabled` visual state does not reach a button whose text colour Spine sets directly, so without it a disabled action looked identical on Android.
- The badge pill uses the same red as the native tab badges (`#FF3B30`) so counts read the same in the tab bar and the header.
- Verified on iPhone 17 (iOS 26.4) and the Pixel 10 Pro emulator: text and badge update in place, disabling dims, hiding Filter lets Bell take the slot, adding a Primary "Cancel" replaces the back button and removing it brings it back.
