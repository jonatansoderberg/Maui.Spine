# Reorder

`Reorder.Mode` lets the user drag the items of any `CollectionView` (a `HeroCollectionView` too) to a new place. The platform's own drag does the work:

- the item lifts and follows the finger;
- the other items make room;
- the list scrolls when the item is held near its top or bottom edge;
- a haptic marks the lift, every new place and the drop.

Screen readers get the same feature without a drag: every item offers **Move up**, **Move down**, **Move to top** and **Move to bottom**.

`Reorder` is in the core package (`Plugin.Maui.Spine.Extensions`). Nothing needs to be registered.

```xml
<CollectionView ItemsSource="{Binding Cards}"
                Reorder.Mode="Handle"
                Reorder.Command="{Binding SaveOrderCommand}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="Card">
            <Grid ColumnDefinitions="*,44" Semantic.Merge="True">
                <Label Text="{Binding Title}" />
                <Image Grid.Column="1" SvgImageSource.Svg="griphorizontal.svg" Reorder.IsHandle="True" />
            </Grid>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

```csharp
public ObservableCollection<Card> Cards { get; } = [...];

// Runs once after the drop. Cards already has the new order.
[RelayCommand]
private Task SaveOrderAsync(ReorderMove move) => _store.SaveOrderAsync(Cards);
```

## Modes

| `ReorderMode` | Behaviour |
|---|---|
| `Off` | The default. Items stay where they are. |
| `LongPress` | A long-press anywhere on an item lifts it. A normal drag still scrolls the list. |
| `Handle` | Only a view marked with `Reorder.IsHandle` starts a drag, and it starts as soon as the handle is touched, the way a table's reorder control does. The rest of the item scrolls and taps as usual. |

Any view in the item template can be the handle: a grip icon (`griphorizontal.svg` in the icon set), a thumbnail or a whole column. Spine shows the handles while they can drag and hides them otherwise, so leave their `IsVisible` to Spine. In `LongPress` mode the handles are hidden, because the whole item is the handle.

`Handle` is the safest mode on a page with the back-swipe, because a drag on a handle never competes with a horizontal swipe.

## Turning it on and off

`Reorder.IsEnabled` says whether the items can move right now, in any mode. It is true by default. While it is false, the handles are hidden, a long-press does nothing and the screen-reader actions are gone.

Use it to turn reordering off while the list loads or saves:

```xml
<CollectionView ItemsSource="{Binding Cards}"
                Reorder.Mode="LongPress"
                Reorder.IsEnabled="{Binding IsLoaded}" />
```

### An Edit button

Bind `Reorder.IsEnabled` to an edit state and toggle it from a header action. The handles then show only while editing, the way a table in edit mode shows its reorder controls:

```xml
<CollectionView ItemsSource="{Binding Cards}"
                Reorder.Mode="Handle"
                Reorder.IsEnabled="{Binding IsEditing}"
                Reorder.Command="{Binding SaveOrderCommand}" />
```

```csharp
[ObservableProperty]
public partial bool IsEditing { get; set; }

[PageAction("Edit")]
[RelayCommand]
private void ToggleEditing() => IsEditing = !IsEditing;
```

## The order

Spine moves the item in `ItemsSource` itself, and the list animates the move. For this, `ItemsSource` must be a list that can change, such as an `ObservableCollection<T>`. An array or a read-only list cannot be reordered, and Spine leaves it alone.

After the drop, `Reorder.Command` runs once with a `ReorderMove`:

| Property | Value |
|---|---|
| `From` | The item's index before the move. |
| `To` | The item's index after the move. |
| `Item` | The item that moved. |

The command is optional. Use it to save the order. A drop on the item's own place, or a cancelled drag, does not run it. A move made through a screen-reader action runs it in the same way.

## Lists and grids

A vertical list moves its items up and down only, and a horizontal list moves them sideways. A grid (`ItemsLayout="VerticalGrid, 2"`) lets an item go anywhere.

A header and a footer stay where they are. Grouped lists (`IsGrouped="True"`) are not supported, and a list that is grouped does not reorder.

## Platforms

| Platform | How it works |
|---|---|
| iOS | `UICollectionView`'s interactive movement. The item scales up slightly and casts a shadow; with Reduce Motion it only casts the shadow. |
| Mac Catalyst | As on iOS. A long-press with the pointer needs only 0.1 seconds. |
| Android | `ItemTouchHelper`. The item scales up slightly, unless animations are turned off. The lift haptic is the one `ItemTouchHelper` plays itself. |
| Windows | `ListView`'s own reordering. A mouse drags at once, so `Handle` works on the whole item. There are no screen-reader actions and no haptics. |

## Screen readers

Spine adds the move actions to each item's root view and to its handle. VoiceOver lists them under **Actions**, and TalkBack lists them in its actions menu. An action is only offered where it does something: the first item has no **Move up**.

Give the item's root view `Semantic.Merge="True"`, so the item is one element and the actions are on it. A handle without a `SemanticProperties.Description` reads as **Reorder**.

The action names are the strings `Spine.Reorder.MoveUp`, `Spine.Reorder.MoveDown`, `Spine.Reorder.MoveToTop`, `Spine.Reorder.MoveToBottom` and `Spine.Reorder.Handle`, in English and Swedish. An app can override them, like any other Spine string (see [Strings](strings.md)).

## MAUI's CanReorderItems

`CollectionView.CanReorderItems` is MAUI's own, simpler reordering. It has no handle, no on/off switch, no haptics and no screen-reader actions. Leave it unset on a list that has a `Reorder.Mode`, because Spine sets it itself where it needs it.

## In the Showcase

The **Reorder** page has every mode and an Edit button, as a list and as a grid, with the code for each combination. The **HeroCollectionView** page has a `LongPress` option.
