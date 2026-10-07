# Context menus in Spine — `ContextMenu.Items` on any view (study, rev 1)

**Status:** Delivery steps 1–3 implemented in #306 (2026-10-07): `ContextMenu.Items`, `ContextMenu.CommandParameter`, `MenuAction.IsVisible`, the Showcase page "Context menus" and docs/wiki/menus.md. Step 4 (`DataGrid.RowContextMenu`) was implemented in [#466](https://github.com/jonatansoderberg/Maui.Spine/issues/466), together with `ContextMenu.Show` for Android, where a row's MAUI recognizer takes the long click; step 5 (`ContextMenu.Preview`) waits for an app that needs it. The owner's decisions from 2026-09-30 are in the [Decisions](#decisions-2026-09-30) section. Issue: [#306](https://github.com/jonatansoderberg/Maui.Spine/issues/306), prioritized as P2 in [#317](https://github.com/jonatansoderberg/Maui.Spine/issues/317) with the questions "Sharing the `PageAction` model, preview support".
**Question:** Can Spine give any view the system's own context menu (long press on touch, right click on desktop) with icons, destructive rows, submenus and, on iOS, a preview, declared with the same model as the header bar's menu buttons, and have it work in list rows?
**Answer:** Yes, but the model to share is **`MenuItems`, not `PageAction`**. #318 already built the shared menu model (`MenuAction`, `MenuSection`, `SubMenu`, `MenuPicker`) and one menu builder per platform. The context menu becomes an attached property, `ContextMenu.Items`, that follows the view's handler the same way `Tap.Command` does, and that reuses #318's builders on every platform: `UIContextMenuInteraction` on iOS and Mac Catalyst, `PopupMenu` on long-click on Android and `UIElement.ContextFlyout` on Windows. A preview exists only on iOS/iPadOS. v1 lifts the view itself, with rounded corners. A custom preview (`ContextMenu.Preview`) is a second step. Android and Windows get no preview, because the platforms have none.

---

## 1. The conclusion in short

| Question | Answer | Evidence |
|---|---|---|
| Share the `PageAction` model? | **No. Share `MenuItems`.** `PageAction` is a header button (`Placement`, `Role`, `Badge`, `IsSelected`, `Haptic`) and has neither `IsDestructive`, `IsChecked` nor submenus. `MenuAction` has all of that, and a `PageAction` already carries a `MenuItems` through `PageAction.Menu`. A context menu and a header menu can therefore be **the same instance**. The issue's sketch `new PageAction("Follow", Icons.Star, FollowCommand)` does not compile against today's `PageAction`, which only has the constructor `(text, command)`. | `PageAction.cs:53`, `:76`, `:101`; `MenuElements.cs:24`; `issues/318-menu-buttons.md` ("Shared with the context-menu idea (#306)") |
| Preview? | **iOS/iPadOS: yes.** `UIContextMenuInteraction` lifts the view (a *targeted preview*) or shows a custom view controller from `previewProvider`. **Mac Catalyst: no.** The Mac menu is "compact": "A nonmodal, compact menu with no preview". **Android, Windows: no**, the system has no equivalent. | The Apple documentation for `UIContextMenuInteraction.appearance`, §4 |
| Is MAUI's `FlyoutBase.ContextFlyout` enough? | **No.** The documentation says "on Mac Catalyst and Windows". On iOS, `MapContextFlyout` is wrapped in `#if MACCATALYST` and so does nothing. On Android the method is empty. The Mac interaction passes `previewProvider: null`. `MenuFlyout` has no destructive or checked rows and cannot be changed at runtime. | §3 |
| Same shape as `Tap.Command`? | **Yes.** An attached property that creates a state object and connects and disconnects it when the view's handler changes. That needs no mapper per control type and no registration. | `Tap.cs:79`, `Tap.cs:113–130` |
| How much of #318 is reused? | **The menu builders as they are**: `BuildMenu` (Apple), `Fill` (Android), `FillFlyout` (Windows), `MenuButton.Pick` and `MenuButton.Icon`. The Android builder currently sits inside a click listener and needs to be lifted out. | `MenuExtensions.Apple.cs:87`, `MenuExtensions.Android.cs:82`, `MenuExtensions.Windows.cs:56`, `MenuButton.cs:59`, `:81` |
| Does it work in list rows? | **Yes, in `CollectionView` and `HeroCollectionView`, through the root of the row template.** The menu is built when it opens, not when the row is bound, so a recycled row costs only one interaction or listener. `DataGrid` needs a property of its own, because its long press already copies the cell's text. | §7 |

---

## 2. What Spine already has

| Part | Where | What it means for context menus |
|---|---|---|
| The menu model | `Core/Menu/MenuElements.cs`: `MenuItems` (`:14`), `MenuAction` (`:24`), `MenuSection` (`:76`), `SubMenu` (`:104`), `MenuPicker` (`:138`) | Title, SVG icon, command and parameter, checkmark, `IsEnabled`, `IsDestructive`, sections, submenus and single-selection groups are there. The only thing missing for context menus is `IsVisible` (§6.5). |
| Change observation | `MenuObserver.cs:10` observes the whole tree and rebuilds the native menu on every change | Not needed in v1: a context menu can be built when it opens and is then always current (§6.3). |
| Picking a row | `MenuButton.Pick(owner, action, picker)` (`MenuButton.cs:59`): flips toggles, moves the picker's checkmark and runs commands | Reused. `owner` is already a `VisualElement`, not a `Button`. The proposal is a fallback parameter for rows in lists (§6.3). |
| Icons | `MenuButton.Icon` (`MenuButton.cs:81`) renders SVG to PNG through `SvgBitmapLoader`. Apple turns it into a template image (`MenuExtensions.Apple.cs:150`), Android picks the color by theme (`MenuExtensions.Android.cs:144`) | The same icons and the same names as in `Plugin.Maui.Spine.Svg.Icons` (`SpineIcons.Star`, `.Share`, `.Copy`, …). |
| The Apple builder | `BuildMenu`/`BuildChildren`/`BuildAction` (`MenuExtensions.Apple.cs:87–142`) produce a `UIMenu` | Can be returned as it is from the `actionProvider` of a `UIContextMenuConfiguration`. |
| The Android builder | `MenuClickListener.OnClick` and `Fill` (`MenuExtensions.Android.cs:56–130`): `PopupMenu`, `SetForceShowIcon` (API 29+), `SetGroupDividerEnabled` (API 28+), red destructive titles | The logic should move out of the click listener into a static `ShowPopup(anchor, items, owner, …)` that both the menu button and the context menu call. |
| The Windows builder | `FillFlyout`/`BuildItem` (`MenuExtensions.Windows.cs:56–117`) produce a `MenuFlyout` that is set as the button's `Flyout` (`:49`) | The same `MenuFlyout` can be set as `UIElement.ContextFlyout` on any view. |
| Registration | `ConfigureMenus()` is called from `ConfigureHandlers` on every platform (`SwitchHandlerExtensions.Apple.cs:16`, `ButtonExtensions.Android.cs:13`, `HandlerExtensions.Windows.cs:10`) | The context menu needs no mapper (see the next row), so nothing is added here except possibly the `Button` case on Apple (§6.6). |
| `Tap.Command` | `Tap.cs:23` (attached property), `TapState` (`Tap.cs:113`) follows `HandlerChanging`/`HandlerChanged` and connects the platform with `ConnectPlatform`/`DisconnectPlatform` | The pattern for "any view": a `ContextMenuState` built the same way. `TapState.CornerRadius()` (`Tap.cs:233`) reads the rounding of a `Border`, which is exactly what the preview's `VisiblePath` needs. |
| Header menus | `PageActionView` passes `PageAction.Menu` to its buttons (`PageActionView.cs:633–635`) | A page's "more" menu and the row's context menu can be the same `MenuItems`, and that is what #317 means by "one menu model serves both". |
| `DataGrid` | A long press on a text cell copies the text (`DataGrid.Press.cs:111–126`, `LongPressDuration` 500 ms in `DataGridStyleOptions.cs:53`). Rows have a `PointerGestureRecognizer` (`DataGrid.Press.cs:62`) and, when needed, a `SwipeView` (`DataGrid.Swipe.cs:23`) | Conflict with the long press. See §7. |

---

## 3. What MAUI 10 has, and where it ends

`FlyoutBase.ContextFlyout` takes a `MenuFlyout` (`MenuFlyoutItem`, `MenuFlyoutSubItem`, `MenuFlyoutSeparator`, `IconImageSource`, keyboard accelerators) and can be set on any `Element`. The documentation (docs-maui, `context-menu.md`) says: *"A context menu can be added to any control that derives from Element, on Mac Catalyst and Windows."* The source on `dotnet/maui` `main` confirms it:

| Platform | What MAUI does | Source |
|---|---|---|
| Windows | `MapContextFlyout` sets `UIElement.ContextFlyout` to the converted `MenuFlyout`. | `ViewHandler.Windows.cs` |
| Mac Catalyst | `MauiUIContextMenuInteraction` (internal) is added to the view. `UIContextMenuConfiguration` is created with `previewProvider: null`, and the delegate implements only `GetConfigurationForMenu`. | `ViewHandler.iOS.cs`, `MauiUIContextMenuInteraction.cs` |
| iOS | The public `MapContextFlyout` has the attribute `SupportedOSPlatform("ios13.0")`, but the body is `#if MACCATALYST`. So nothing happens on iPhone and iPad. | `ViewHandler.iOS.cs` |
| Android | `public static void MapContextFlyout(IViewHandler handler, IView view) { }`: empty. | `ViewHandler.Android.cs` |

Documented limitations beyond the platforms: *"Mac Catalyst does not support displaying icons on context menu items"*, and *"It's not currently possible to add items to, or remove items from, the MenuFlyout at runtime"*. `MenuFlyoutItem` has neither a destructive style nor a checkmark, and there is no single-selection group.

The conclusion: MAUI's route is a second menu model that lacks what #318 already has, and it does not cover the two platforms where context menus are used the most. Building on it would give Spine two menu models.

---

## 4. The platforms' building blocks

### 4.1 iOS and iPadOS: `UIContextMenuInteraction`

Checked against Apple's documentation and against the bindings in `Microsoft.iOS.Ref.net10.0_26.2` 26.2.10217, the same SDK line the repository builds with:

| API | Available | In Microsoft.iOS |
|---|---|---|
| `UIContextMenuInteraction(delegate)`, `view.AddInteraction` | iOS 13 | `new UIContextMenuInteraction(IUIContextMenuInteractionDelegate)` |
| `UIContextMenuConfiguration(identifier:previewProvider:actionProvider:)` | iOS 13 | `UIContextMenuConfiguration.Create(INSCopying, UIContextMenuContentPreviewProvider, UIContextMenuActionProvider)` |
| Lift preview: `configuration:highlightPreviewForItemWithIdentifier:` | iOS 16 (the older `previewForHighlightingMenuWithConfiguration:` is deprecated from 16) | `GetHighlightPreview` (ios16.0); `GetPreviewForHighlightingMenu` (ObsoletedOSPlatform ios16.0) |
| Tap on the preview: `willPerformPreviewActionForMenuWith:animator:` | iOS 13 | `WillPerformPreviewAction` |
| The shape of the lift: `UIPreviewParameters.VisiblePath`, `.BackgroundColor`, `.ShadowPath` | iOS 13 | Yes |
| Update an open menu: `UpdateVisibleMenu` | iOS 14 | Yes |
| `menuAppearance`: `.rich` ("A modal menu with an optional preview") / `.compact` ("A nonmodal, compact menu with no preview") | iOS 14 | `MenuAppearance` |
| `UIButton.menu` with `showsMenuAsPrimaryAction = false` | iOS 14 | The menu becomes the button's context menu (long press). According to Apple's documentation, `menu` turns the button's `contextMenuInteraction` on and off automatically. |

Spine's minimum is iOS 15 (`docs/wiki/packages.md`), so the new lift method (16) needs a fallback to the old one on iOS 15, or iOS 15 gets the default lift without rounded corners. The latter is simpler and is enough.

Three ways to show the preview, all of which fit in the same delegate:

1. **Nothing specified**: the system lifts the view as it is, with rectangular corners.
2. **A targeted preview of the view itself** with `VisiblePath` = the view's rounded shape and `BackgroundColor` = the view's background. This is what Mail and Notes do with list rows, and what the HIG asks for: *"adjust the preview's clipping path to match the shape of the preview image so that its contours, such as the rounded corners, don't appear to change during animation."*
3. **A custom view controller** from `previewProvider`: a different and larger view than the one that was pressed, for example a competition's detail card. A tap on it runs `WillPerformPreviewAction`, which usually navigates to what was previewed.

### 4.2 Mac Catalyst

The same `UIContextMenuInteraction`, opened with a right click or Ctrl-click. In the Mac idiom the menu becomes `compact`: no preview, no lift. Icons are shown in a `UIMenu` on Catalyst (MAUI's "no icons" limitation applies to their `MenuFlyout` conversion, not to UIKit), but that is **not verified** in Spine's builder. MAUI adds its own `MauiUIContextMenuInteraction` to the view only when the app has set `FlyoutBase.ContextFlyout`. An app that sets both gets two interactions, which should be documented as "don't do that".

### 4.3 Android

Android has no system context menu with a preview, and the classic context menu shows no icons. According to the Android documentation (*Menus*): *"Context menu do not support item shortcuts and item icons."* The building blocks, checked against `Mono.Android.dll` 36.1.43:

| API | Minimum API level | Use |
|---|---|---|
| `View.SetOnLongClickListener` | 1 | Long press on touch. If the listener returns `true`, no click runs, so `Tap.Command` on the same view does not run afterwards. |
| `View.SetOnContextClickListener` | 23 | Right click with a mouse or stylus button (Chromebook, DeX, tablet with a mouse). |
| `PopupMenu(context, anchor)` + `SetForceShowIcon` | 29 for icons | The same menu as #318's menu button. Anchored below the view if there is room, otherwise above. |
| `PopupMenu.Gravity` | 23 | Alignment against the anchor (start/end). |
| `View.ShowContextMenu(x, y)` | 24 | The classic floating context menu at the touch point. **No icons**, and it requires `OnCreateContextMenu`. |
| `IMenu.SetGroupDividerEnabled` | 28 | Dividers between sections, as today. |

`PopupMenu` is the route that gives icons, red destructive rows and the same look as the menu button. The documentation describes it as the menu for *"actions that relate to specific content"* and distinguishes it from the context menu *"for actions that affect selected content"*. The difference is mostly terminology in Spine's case: the row is the content. What differs in practice is the placement. A `PopupMenu` anchored to a whole card appears below the card, not at the finger. A known trick is an invisible 1×1 anchor at the touch point, but it is **not tried** here and should be judged on a device before it is chosen.

Material 3 has no context menu of its own in the Views library to lean on. Material Components has menus (`ListPopupWindow`, exposed dropdown menus), but nothing with a lift or a preview. The third-party variant that makes "iOS-like" context menus on Android (The49.Maui.ContextMenu) draws the preview itself. That is the same choice Spine has turned down for tabs and sheets in favor of the native one.

### 4.4 Windows

`UIElement.ContextFlyout` shows the flyout on a right click or *"an equivalent action, such as pressing and holding with your finger"*, and marks `ContextRequested` as handled. Spine's `FillFlyout` already produces the right `MenuFlyout` (radio buttons for pickers, toggles, submenus, separators). The menu can be built when it opens through `MenuFlyout.Opening`. There is no preview on Windows.

---

## 5. The alternatives

| | A. Spine: `ContextMenu.Items` (`MenuItems`) | B. Build on MAUI's `FlyoutBase.ContextFlyout` | C. Third-party package | D. List level (`UICollectionViewDelegate`, `registerForContextMenu(RecyclerView)`) |
|---|---|---|---|---|
| Model | The existing one (#318), shared with header menus | A second model (`MenuFlyout`) without destructive, checkmark or picker | The package's own (`the49:Menu`/`Action`) | A's model |
| iOS | Own `UIContextMenuInteraction` | Needs an iOS mapping of its own anyway: MAUI's interaction is internal and switched off | Yes | The best preview for cells (the iOS 16 API `GetContextMenuConfiguration(collectionView, indexPaths, point)`) |
| Android | `PopupMenu`, like the menu button | Everything has to be built; MAUI does nothing | Self-drawn preview | Classic menu without icons |
| Windows | `ContextFlyout` | Works today | Not supported (The49) | — |
| Preview | iOS: lift in v1, custom template in step 2 | None (`previewProvider: null`) | iOS and Android | iOS |
| Risk | Low: the builders exist and are verified on simulator and emulator | High: taking over a MAUI mapping that MAUI can change | Dependency: The49 exists only as `1.0.0-alpha1` for net7 (unlisted). `DSoft.Maui.ContextMenu` 1.1.2606.161 (net10, 2026-06-16) has the same description; its origin is not checked | High: requires replacing MAUI's internal delegator for `CollectionView` |

**A.** D is interesting for the preview in lists, but it assumes that Spine takes over the `UICollectionView` delegate that MAUI owns. The per-view interaction on the root of the row template gives the same menu and a lift of the row. That is enough until someone points to a difference that matters.

---

## 6. Proposed design

### 6.1 API

```csharp
namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Gives any view the platform's own context menu: a long press on touch, a right click on desktop.
/// iOS and iPadOS lift the view (or show <see cref="PreviewProperty"/>); Mac Catalyst, Android and Windows show the menu only.
/// </summary>
public static class ContextMenu
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.CreateAttached("Items", typeof(MenuItems), typeof(ContextMenu), null,
            propertyChanged: OnItemsChanged);          // creates/disposes ContextMenuState, like Tap

    /// <summary>Passed to an action's command when the action has no CommandParameter of its own.</summary>
    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.CreateAttached("CommandParameter", typeof(object), typeof(ContextMenu), null);

    // Step 2, iOS/iPadOS only:
    public static readonly BindableProperty PreviewProperty =           // DataTemplate
        BindableProperty.CreateAttached("Preview", typeof(DataTemplate), typeof(ContextMenu), null);
    public static readonly BindableProperty PreviewCommandProperty =    // a tap on the preview
        BindableProperty.CreateAttached("PreviewCommand", typeof(ICommand), typeof(ContextMenu), null);
}
```

The name `ContextMenu` does not clash in XAML: `Microsoft.Maui.Controls` 10.0.50 has no type with that name (checked in `Microsoft.Maui.Controls.dll`), unlike `Menu`, which was the reason for `MenuButton` in #318.

### 6.2 Usage

```xml
<!-- a card with a menu; Tap.Command still opens it on a plain tap -->
<Border Tap.Command="{Binding OpenCommand}" Semantic.Merge="True"
        ContextMenu.Items="{Binding CardMenu}">
    ...
</Border>

<!-- a row in a list: one shared menu, the row's item as the parameter -->
<CollectionView ItemsSource="{Binding Competitions}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="m:Competition">
            <Grid ContextMenu.Items="{Binding RowMenu, Source={RelativeSource AncestorType={x:Type vm:CompetitionsViewModel}}}"
                  ContextMenu.CommandParameter="{Binding .}">
                ...
            </Grid>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

```csharp
public MenuItems RowMenu { get; } =
[
    new MenuSection
    {
        new MenuAction("Följ", SpineIcons.Star, FollowCommand),
        new MenuAction("Lägg till i kalendern", SpineIcons.Calendar, AddToCalendarCommand),
        new MenuAction("Dela", SpineIcons.Share, ShareCommand),
    },
    new MenuAction("Ta bort", SpineIcons.Delete, RemoveCommand) { IsDestructive = true },
];

[RelayCommand] private Task Follow(Competition competition) { ... }
```

The same `RowMenu` can sit on the detail page's header with `new PageAction(null, RowMenu) { Svg = "more.svg" }`. The row and the page then get the same actions, which the HIG also asks for: *"Always make context menu items available in the main interface, too."*

### 6.3 Built when it opens, and the parameter

The menu button in #318 builds its native menu right away and rebuilds it on every change through `MenuObserver`. For the context menu the opposite is proposed: **the menu is built every time it opens.** All three platforms offer a hook at opening: `actionProvider` runs on a long press on Apple, Android builds the `PopupMenu` in the long-click listener and Windows has `MenuFlyout.Opening`. Two consequences:

- A row in a list costs one interaction or one listener, not a `MenuObserver` and a menu tree per row.
- The row's state is read at opening. A row that has changed item (a recycled cell) shows the right menu.

`ContextMenu.CommandParameter` makes a shared menu usable for many rows. The rule in `Pick` becomes `action.CommandParameter ?? ContextMenu.GetCommandParameter(owner)`. A picker keeps its rule (the row's parameter or the row itself). It is a small change in `MenuButton.Pick` (`MenuButton.cs:73`), a fallback parameter that the menu button passes as `null`.

Rows whose text depends on the item ("Follow"/"Unfollow") are handled in one of two ways: the item exposes a `MenuItems` of its own (cheap, since nothing is built before the menu opens), or two rows where only one is visible (§6.5). A `ContextMenu.Opening` command that lets the view model change a shared menu just before it opens is possible on all three platforms, but is not added before an app needs it.

### 6.4 Preview (iOS/iPadOS)

**Step 1 (v1): lift the view with the right shape.** `GetHighlightPreview` (iOS 16+) returns `new UITargetedPreview(view, parameters)` where `parameters.VisiblePath` is a rounded rectangle with the corner radius of the `Border`. It is the same calculation that `TapState.CornerRadius()` does, so it should be shared. `BackgroundColor` is set to the view's background, otherwise `SystemBackground`, because a row template is often transparent and the lift then looks empty at the edges. On iOS 15 the default lift is used. `GetDismissalPreview` returns the same, so that the view lands where it came from.

**Step 2: `ContextMenu.Preview` + `PreviewCommand`.** `previewProvider` creates a `UIViewController` whose view is the template's MAUI view converted with `ToPlatform(mauiContext)`, measured with `Measure` and sized with `PreferredContentSize`. The template gets the view's `BindingContext`. `WillPerformPreviewAction` runs `PreviewCommand` in the animator's completion. That is how "tap the preview to open" works in Mail and Photos. This step carries the largest technical uncertainty: a MAUI view whose handler lives outside the page's tree, and which has to be disconnected when the menu closes (`WillEnd`). That is why it is a step of its own.

Android and Windows ignore both properties. That should be stated in the XML documentation, not only in the wiki.

### 6.5 Change to the shared model: `MenuAction.IsVisible`

HIG: *"Hide unavailable menu items, don't dim them. Unlike a regular menu, … a context menu displays only the actions that are relevant."* The model only has `IsEnabled` (`MenuElements.cs:59–61`). The proposal is `IsVisible` (default `true`) on `MenuAction` and `SubMenu`, and that all three builders skip invisible rows. It then applies to menu buttons too. It is needed for "Follow/Unfollow" with a shared menu, and it is the only change to #318's public surface.

### 6.6 Interplay with what already exists

| Combination | Behavior |
|---|---|
| `Tap.Command` + `ContextMenu.Items` | The common case: a tap opens, a long press shows the menu. **Apple:** `PressRecognizer` recognizes simultaneously with everything (`Tap.Apple.cs:174`) and shows its highlight after 70 ms (`:16`). The highlight is a `CALayer` in the view (`:131`) and so comes along in the lift unless it is cleared. `ContextMenuState` should call `TapState.CancelPress()` when the menu is shown (`WillDisplayMenu`). Whether the interaction cancels the touch so that the command does not run on release is **not verified**. **Android:** a long-click that returns `true` prevents the click; the ripple (`Tap.Android.cs:31`) is visible during the hold, as in system apps. **Windows:** a right click gives no `Tapped`; the behavior for press-and-hold is **not verified**. |
| `MenuButton.Items` + `ContextMenu.Items` on a `Button` | Apple: both use `UIButton.Menu`. `ShowsMenuAsPrimaryAction = true` means tap, `false` means long press. A button cannot have both, and **the menu button wins**. A `Button` with only `ContextMenu.Items` gets its menu as `button.Menu` with `ShowsMenuAsPrimaryAction = false`, not an extra interaction, because `UIControl` has a `contextMenuInteraction` of its own. Android and Windows have no conflict (click versus long-click listener, `Flyout` versus `ContextFlyout`). |
| `FlyoutBase.ContextFlyout` + `ContextMenu.Items` | Windows and Catalyst: two mechanisms on the same view. Documented as "pick one". Spine sets `ContextFlyout` on Windows and overwrites MAUI's if both are present. |
| Accessibility | Apple: whether VoiceOver offers the menu automatically for a view with a `UIContextMenuInteraction` is **not checked**. Android: the long-click action gets a label through `ViewCompat.ReplaceAccessibilityAction(ActionLongClick, "Actions")` from `SpineStrings`, so that TalkBack says what a long press does. Exposing every menu row as an accessibility action of its own as well is possible and worth trying. |

---

## 7. Lists: `CollectionView`, `HeroCollectionView` and `DataGrid`

**`CollectionView` and `HeroCollectionView`.** `HeroCollectionView` is a `CollectionView` (`HeroCollectionView.cs:22`) and needs nothing of its own. `ContextMenu.Items` is set on the root of the row template. Apple adds the interaction to the MAUI view in the cell's `ContentView`, Android to the row's view in the `RecyclerView`, Windows to the element in the list item. Cell recycling is harmless because the menu is built at opening (§6.3) and the parameter is read from the binding that applies at that moment. Scrolling cancels the long press on all three platforms, because it is the system's own gesture. Still to check on a device: that the selection highlight of `CollectionView` (`SelectionMode`) and the lift do not clash on iOS, and that the long-click reaches the row view on Android when the row template has MAUI gesture recognizers. MAUI's gesture handling on Android sets its own touch listeners. It is **untested** whether they consume the long-click.

**`DataGrid`.** Rows are built in code, not from a template, and already have a long press: a text cell is copied (`DataGrid.Press.cs:111–126`). Putting `ContextMenu.Items` on the row from outside is not possible, and if it were, two long presses would fight over the same finger. The proposal is a property on the grid:

```xml
<spine:DataGrid ItemsSource="{Binding Games}" RowContextMenu="{Binding GameMenu}" />
```

- With `RowContextMenu` set, a long press opens the menu with the row's item as the parameter, and the copy becomes a row in the menu ("Copy *Column name*" at the top), since the grid already knows which cell the finger hit (`FindCell`, `DataGrid.Press.cs:82`). Without `RowContextMenu` everything is as it is today.
- On Apple the grid gives the row a `UIContextMenuInteraction` and turns off its own long-press timer for that row. On Android and Windows the grid can open the menu itself from `OnRowLongPress`, through the same `ShowPopup`/`ContextFlyout.ShowAt` that `ContextMenuState` uses.
- Swipe actions (`LeftSwipeActions`/`RightSwipeActions`) are a different gesture and stay. They have a model of their own (`DataGridSwipeAction`, `DataGridSwipeAction.cs:15`) with colors and path bindings. Turning them into `MenuAction` is not part of this.

How `DataGrid`'s own press handling behaves together with a `UIContextMenuInteraction` on the same row is the most uncertain part of the whole proposal. That is why it is a step of its own (§10).

---

## 8. Platforms

| Platform | v1 | Preview | Note |
|---|---|---|---|
| iOS 16+, iPadOS 16+ | `UIContextMenuInteraction`, `UIMenu` from #318's builder | Lift of the view with a rounded `VisiblePath`; custom template in step 2 | iPad with a mouse or trackpad: a right click gives the compact menu |
| iOS 15 | Same | Default lift (rectangular) | The old lift method can be added if someone misses it |
| Mac Catalyst | Same interaction, right click | No (compact) | Icons in the menu not verified |
| Android 5–9 (API 21–28) | `PopupMenu` on long-click; right click from API 23 | No | Icons are shown from API 29 (`SetForceShowIcon`), dividers from 28 |
| Android 10+ | Same, with icons | No | The menu is anchored to the view, not at the finger |
| Windows | `ContextFlyout` with #318's `MenuFlyout` | No | Right click and press-and-hold give the system's own behavior |

---

## 9. What cannot be done, and what is not verified

The study was done in a Linux container without a Mac, simulator, emulator or device. **Nothing in it has been tried on a platform.** API availability is checked against Apple's documentation, the `Microsoft.iOS` reference 26.2.10217, `Mono.Android` 36.1.43 and MAUI's source on `main`. Behavior is not.

1. **No preview on Android, Windows or Mac.** The platforms have none. Drawing one ourselves (like The49) goes against the line of native controls and is advised against.
2. **The tap highlight in the lift** (§6.6): reasoning from the code, not observed. The same goes for whether `Tap.Command` runs when the finger is released after the menu has opened.
3. **Long-click on Android under MAUI's gesture recognizers** (§7): untested. If MAUI's touch listeners consume the events, `ContextMenuState` needs a `GestureDetector` of its own, which is more code than planned.
4. **The placement of `PopupMenu`** on large views, and the trick with an anchor at the touch point: untested.
5. **Section titles in context menus on Apple.** #318 found that UIKit does not draw the title of inline sections in a *button menu*. Whether the same holds in a context menu is not checked.
6. **Custom preview template** (§6.4 step 2): a MAUI view with a handler outside the page's tree, in a view controller that UIKit owns. Measuring, theme changes and disconnecting at `WillEnd` are untested.
7. **VoiceOver and the menu** (§6.6): not checked whether it is offered automatically.
8. **Icon rendering at opening.** `MenuButton.Icon` renders SVG synchronously (`MenuButton.cs:93`). On a long press that happens in `actionProvider`, that is, on the way into the animation. With a handful of icons it should be fast, but it is not measured. A small cache per `(svg, size, color)` is one line of code if it is needed.
9. **An observation in passing:** the Windows builder renders icons black regardless of theme (`MenuExtensions.Windows.cs:121`), while Android picks the color by theme (`MenuExtensions.Android.cs:144`). If `ImageIcon` shows the bitmap as it is, the icons become hard to see in dark theme, both in today's menu buttons and in the context menu. Not checked on Windows.

---

## 10. Delivery plan

1. **Share out the builders.** Android: move `Fill`/`AddAction`/`Icon` out of `MenuClickListener` into a static `ShowPopup(anchor, items, owner, parameter)`. `MenuButton.Pick` gets the fallback parameter. `MenuAction.IsVisible`/`SubMenu.IsVisible` are added and respected by all three builders. The menu buttons should behave exactly as before. That is verified in `MenusPage`.
2. **`ContextMenu.Items` + `CommandParameter`.** `ContextMenu` and `ContextMenuState` (the pattern from `TapState`). Apple: interaction, `GetConfigurationForMenu` with `BuildMenu` at opening, lift with `VisiblePath` on iOS 16+, the `Button` case through `UIButton.Menu`, and clearing the tap highlight. Android: long-click, context-click (API 23+) and accessibility label. Windows: `ContextFlyout` built in `Opening`.
3. **Sample and wiki.** A page `Pages/ContextMenus` in `MauiSpineSampleApp`: a card with `Tap.Command` and a menu, a `CollectionView` with a shared row menu and parameter, the same menu on the header bar. After that a section in `docs/wiki/menus.md` (not a new page: it is the same model) and a line in `/spine-controls`. Verify on the iPhone simulator, the Pixel emulator and Mac Catalyst; Windows through CI.
4. **`DataGrid.RowContextMenu`** (§7), with copying as a menu row. A step of its own because of the conflict with today's long press.
5. **`ContextMenu.Preview` + `PreviewCommand`** (§6.4 step 2), when an app shows a concrete need. Orientera's competition row is the likely first.
6. **The apps.** Orientera (Follow, Add to calendar, Share), Puckkoll (Watch, Open in the SHL app), Almanacka (Copy name, Share). The proof that one declaration is enough for header and row.

Decisions that need the owner before step 2: that `PageAction` does not become the row model (§1); `IsVisible` in the shared model (§6.5); and whether `DataGrid`'s copying should move into the menu when there is a menu (§7).

---

## Decisions (2026-09-30)

Jonatan went through the study's questions on 2026-09-30 and followed the recommendations. Rows marked **Proposal** had no recommendation in the study; they carry a proposal with reasons, which holds until he says otherwise.

- **The model.** `MenuItems` from #318 is shared between header, menu button and context menu. `PageAction` does not become the row model (§1).
- **`IsVisible`.** Added to `MenuAction` and `SubMenu` in the shared model, and then applies to menu buttons too (§6.5).
- **`DataGrid`.** When `RowContextMenu` is set, the long-press copy moves into the menu as a row `Copy <column>` (§7).
- **The surface.** An attached property of its own, `ContextMenu.Items` (alternative A), not MAUI's `FlyoutBase.ContextFlyout`, a third-party package or delegates at list level.
- **Preview.** The view itself is lifted in v1 (iOS 16+, default lift on iOS 15). `ContextMenu.Preview` and `PreviewCommand` become step 2 when an app needs them.
- **Other platforms.** No self-drawn preview on Android, Windows or Mac.
- **Android.** `PopupMenu` anchored to the view. Whether the anchor should be a 1×1 view at the touch point is decided on a device.
- **Button with both `MenuButton.Items` and `ContextMenu.Items`.** The menu button wins.
- **`ContextMenu.Opening`.** Left out until an app needs it.
- **Documentation.** In `docs/wiki/menus.md`, no new wiki page.

---

## 11. References

- Issue #306, *Native context menus on any view*: https://github.com/jonatansoderberg/Maui.Spine/issues/306
- Issue #317, prioritized backlog: https://github.com/jonatansoderberg/Maui.Spine/issues/317
- Issue #318, menu buttons (implemented): https://github.com/jonatansoderberg/Maui.Spine/issues/318, `issues/318-menu-buttons.md`, `docs/wiki/menus.md`
- Apple, `UIContextMenuInteraction`: https://developer.apple.com/documentation/uikit/uicontextmenuinteraction
- Apple, `UIContextMenuInteractionDelegate` (including `configuration:highlightPreviewForItemWithIdentifier:`, iOS 16): https://developer.apple.com/documentation/uikit/uicontextmenuinteractiondelegate
- Apple, `UIContextMenuConfiguration.init(identifier:previewProvider:actionProvider:)`: https://developer.apple.com/documentation/uikit/uicontextmenuconfiguration/init(identifier:previewprovider:actionprovider:)
- Apple, `UIContextMenuInteraction.appearance` (`rich`/`compact`): https://developer.apple.com/documentation/uikit/uicontextmenuinteraction/appearance
- Apple, `UIPreviewParameters.visiblePath`: https://developer.apple.com/documentation/uikit/uipreviewparameters/visiblepath
- Apple, `UICollectionViewDelegate.collectionView(_:contextMenuConfigurationForItemsAt:point:)`: https://developer.apple.com/documentation/uikit/uicollectionviewdelegate/collectionview(_:contextmenuconfigurationforitemsat:point:)
- Apple, `UIButton.menu` and `UIControl.showsMenuAsPrimaryAction`: https://developer.apple.com/documentation/uikit/uibutton/menu, https://developer.apple.com/documentation/uikit/uicontrol/showsmenuasprimaryaction
- Apple HIG, *Context menus*: https://developer.apple.com/design/human-interface-guidelines/context-menus
- Android, *Add menus* (context menus without icons, `PopupMenu`): https://developer.android.com/develop/ui/views/components/menus
- MAUI documentation, *Display a context menu* (source in docs-maui): https://github.com/dotnet/docs-maui/blob/main/docs/user-interface/context-menu.md
- MAUI sources on `main`: `src/Core/src/Handlers/View/ViewHandler.iOS.cs`, `ViewHandler.Android.cs`, `ViewHandler.Windows.cs`, `src/Core/src/Platform/iOS/MauiUIContextMenuInteraction.cs` (https://github.com/dotnet/maui)
- Microsoft, `UIElement.ContextFlyout`: https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.uielement.contextflyout (via search results; the page could not be fetched from the container)
- The49.Maui.ContextMenu: https://github.com/the49code/The49.Maui.ContextMenu, NuGet `1.0.0-alpha1` (net7)
- DSoft.Maui.ContextMenu: https://www.nuget.org/packages/DSoft.Maui.ContextMenu (1.1.2606.161, net10)
- Bindings checked in: `Microsoft.iOS.Ref.net10.0_26.2` 26.2.10217, `Microsoft.Android.Ref.36` 36.1.43, `Microsoft.Maui.Controls.Core` 10.0.50 (NuGet)
