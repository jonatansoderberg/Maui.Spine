# Menu buttons and context menus

A button that opens the platform's own menu on tap: `UIButton.Menu` on iOS and Mac Catalyst (a
glass button morphs into its menu on iOS 26), a `PopupMenu` on Android, a `MenuFlyout` on
Windows. Any view can also have a context menu that opens on a long press or a right click
(see [Context menus](#context-menus)). One declaration, rendered natively everywhere, with
icons, checkmarks, sections, submenus and destructive rows.

## The model

| Type | What it is |
|---|---|
| `MenuItems` | The root collection a menu shows. Observable: change it and the menu follows. |
| `MenuAction` | One row: `Title`, `Svg`, `Command`, `CommandParameter`, `IsChecked`, `IsEnabled`, `IsVisible`, `IsDestructive`, `KeepsMenuOpen`. |
| `MenuSection` | An inline group with separators and an optional title. |
| `SubMenu` | A row that opens a nested menu. `IsVisible` hides it with its rows. |
| `MenuPicker` | A single-selection group: one row checked, a pick moves the check, sets `Selected` and runs the picker's `Command` with the row's `CommandParameter` (or the row itself). |

```csharp
var filter = new MenuPicker(FilterCommand)
{
    new MenuAction("All items", "house.svg") { IsChecked = true },
    new MenuAction("Favourites", "star.svg"),
    new MenuAction("Edited", "edit.svg"),
};

var menu = new MenuItems
{
    new MenuSection("Filter:") { filter },
    new MenuSection
    {
        new SubMenu("Media types", "fish.svg")
        {
            new MenuAction("Photos") { IsChecked = true, KeepsMenuOpen = true },
            new MenuAction("Videos") { KeepsMenuOpen = true },
        },
        new MenuAction("Reset", "refresh.svg", ResetCommand) { IsDestructive = true },
    },
};
```

`KeepsMenuOpen` makes a row a toggle: the pick flips `IsChecked` and the menu stays open on
iOS 16+ and Windows. Android closes the menu on every pick, so a toggle there is a tap and a reopen.

`IsVisible = false` leaves a row out of the menu. `IsEnabled = false` keeps it, dimmed. A context
menu shows only what applies, so it hides rows rather than dimming them, for example "Follow"
and "Unfollow" with one of them visible.

## On a header-bar action

```csharp
PageActions.Add(new PageAction(null, menu) { Svg = "more.svg" });
PageActions.Add(new PageAction("Sort", sortMenu) { MenuShowsSelection = true });
```

A `PageAction` built with a menu has no command; the menu is its action. With
`MenuShowsSelection` a text action takes the picked row's title.

## On a button in the page

```xml
<Button Text="Name" Glass.Style="Regular"
        MenuButton.Items="{Binding SortMenu}" MenuButton.ShowsSelection="True" />

<ImageButton SvgImageSource.Svg="more.svg" SvgImageSource.Padding="10" Glass.Style="Regular"
             WidthRequest="44" HeightRequest="44" MenuButton.Items="{Binding MoreMenu}" />
```

`MenuButton.Items` works on `Button` and `ImageButton`. Give a menu button no `Command`: on
Android and Windows the platform would run it under the menu, on iOS it never fires. With
`MenuButton.ShowsSelection` the button's `Text` follows the picked row, which makes a pop-up
button for "Sort by" and "Season" style choices.

## Changing a menu while it exists

Every `MenuAction`, section, submenu and picker is observable, and the collections are
`ObservableCollection`s. Set `IsChecked`, `Title` or `IsEnabled`, add or remove rows, or set a
picker's `Selected`, and the native menu is rebuilt for its next opening.

## Context menus

`ContextMenu.Items` gives any view the system's own context menu, opened with a long press on
touch or a right click with a mouse. It takes the same `MenuItems`, so a list row and its detail
page's header action can share one instance.

```xml
<Border Tap.Command="{Binding OpenCommand}" ContextMenu.Items="{Binding CardMenu}">
    ...
</Border>

<CollectionView ItemsSource="{Binding Races}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="Race">
            <Border ContextMenu.Items="{PageBinding RowMenu}"
                    ContextMenu.CommandParameter="{Binding .}">
                ...
            </Border>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

```csharp
public MenuItems RowMenu { get; } =
[
    new MenuSection
    {
        new MenuAction("Copy name", "copy.svg", CopyCommand),
        new MenuAction("Share", "share.svg", ShareCommand),
    },
    new MenuAction("Remove", "trashcan.svg", RemoveCommand) { IsDestructive = true },
];

[RelayCommand]
private void Remove(Race race) => Races.Remove(race);
```

- **One menu for every row.** A picked row's command gets its own `CommandParameter` or, when
  that is null, the view's `ContextMenu.CommandParameter`. Put the menu on the root of the row
  template.
- **Built when it opens.** Nothing is built or watched until the long press, so a row costs one
  interaction or listener, and a recycled row shows its current item. A disabled view, or one
  whose rows are all hidden, opens nothing.
- **With `Tap.Command`.** A tap still runs the command. A long press opens the menu and the tap
  does not follow. On iOS the press highlight is cleared before the view is lifted.
- **On a `Button`.** The context menu becomes the button's `UIButton.Menu` on Apple, opened with
  a long press. If the button also has `MenuButton.Items`, the menu button wins.
- **The preview.** iOS and iPadOS 16+ lift the view with its own shape: rounded like a `Border`'s
  `RoundRectangle`, filled with its background (or the system background when it has none). iOS
  15 lifts a plain rectangle. Mac Catalyst, Android and Windows show the menu without a preview,
  because the platforms have none.
- **Don't mix it with `FlyoutBase.ContextFlyout`** on the same view. Windows and Mac Catalyst
  would then have two mechanisms fighting over the right click.

## Platform notes

| Platform | Building block | Notes |
|---|---|---|
| iOS 14+, Mac Catalyst | `UIButton.Menu`, `ShowsMenuAsPrimaryAction` | Pickers use `SingleSelection` (15+), toggles `KeepsMenuPresented` (16+), pop-up buttons `ChangesSelectionAsPrimaryAction` (15+). Glass buttons morph on iOS 26. A section's title is not drawn in a button menu; the separator is. The pop-up chevron's width is added to the button's trailing padding once, so the title does not wrap. |
| Android | `PopupMenu` anchored to the button | Icons shown on API 29+, group dividers on 28+. Sections are groups, pickers exclusive checkable groups, destructive rows red, icon included. The menu closes on every pick. |
| Windows | `MenuFlyout` as the button's `Flyout` | `RadioMenuFlyoutItem` for pickers, `ToggleMenuFlyoutItem` for toggles, `MenuFlyoutSubItem`, separators between sections. |

Context menus:

| Platform | Building block | Notes |
|---|---|---|
| iOS, iPadOS | `UIContextMenuInteraction` | Long press. The view lifts with its rounded shape on 16+. A right click with a mouse or trackpad on iPad gives the compact menu. |
| Mac Catalyst | `UIContextMenuInteraction` | Right click or Ctrl-click. A compact menu at the pointer with icons, no preview. |
| Android | `PopupMenu` anchored to the view | Long press, and right click on API 23+. It shows below the view, or above it when there is no room. TalkBack offers the long press as "Show actions" (`Spine.ContextMenu.Open`). |
| Windows | `UIElement.ContextFlyout` | Right click, or press and hold with a finger or pen. The flyout is filled when it opens. |

Icons are the same SVG names as everywhere else in Spine (`Plugin.Maui.Spine.Svg.Icons` or the
app's own embedded SVGs), rendered as template images so the menu tints them.
