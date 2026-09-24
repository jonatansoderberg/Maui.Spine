# Menu buttons

A button that opens the platform's own menu on tap: `UIButton.Menu` on iOS and Mac Catalyst (a
glass button morphs into its menu on iOS 26), a `PopupMenu` on Android, a `MenuFlyout` on
Windows. One declaration, rendered natively everywhere, with icons, checkmarks, sections,
submenus and destructive rows.

## The model

| Type | What it is |
|---|---|
| `MenuItems` | The root collection a button shows. Observable: change it and the menu follows. |
| `MenuAction` | One row: `Title`, `Svg`, `Command`, `CommandParameter`, `IsChecked`, `IsEnabled`, `IsDestructive`, `KeepsMenuOpen`. |
| `MenuSection` | An inline group with separators and an optional title. |
| `SubMenu` | A row that opens a nested menu. |
| `MenuPicker` | A single-selection group: one row checked, a pick moves the check, sets `Selected` and runs the picker's `Command` with the row's `CommandParameter` (or the row itself). |

```csharp
var filter = new MenuPicker(FilterCommand)
{
    new MenuAction("All items", "house.svg") { IsChecked = true },
    new MenuAction("Favourites", "done.svg"),
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

## Platform notes

| Platform | Building block | Notes |
|---|---|---|
| iOS 14+, Mac Catalyst | `UIButton.Menu`, `ShowsMenuAsPrimaryAction` | Pickers use `SingleSelection` (15+), toggles `KeepsMenuPresented` (16+), pop-up buttons `ChangesSelectionAsPrimaryAction` (15+). Glass buttons morph on iOS 26. A section's title is not drawn in a button menu; the separator is. The pop-up chevron's width is added to the button's trailing padding once, so the title does not wrap. |
| Android | `PopupMenu` anchored to the button | Icons shown on API 29+, group dividers on 28+. Sections are groups, pickers exclusive checkable groups, destructive rows red. The menu closes on every pick. |
| Windows | `MenuFlyout` as the button's `Flyout` | `RadioMenuFlyoutItem` for pickers, `ToggleMenuFlyoutItem` for toggles, `MenuFlyoutSubItem`, separators between sections. |

Icons are the same SVG names as everywhere else in Spine (`Plugin.Maui.Spine.Svg.Icons` or the
app's own embedded SVGs), rendered as template images so the menu tints them.
