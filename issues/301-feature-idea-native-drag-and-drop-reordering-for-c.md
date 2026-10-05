# Issue #301 — Feature idea: Native drag-and-drop reordering for CollectionView and HeroCollectionView

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/301
**Branch:** issue/301-feature-idea-native-drag-and-drop-reordering-for-c
**Status:** Completed

## Plan

### What MAUI already does (10.0.50)
`CollectionView.CanReorderItems` exists and the Showcase's Hero page already uses it. Reading the
MAUI source shows what it does not give us:

- **iOS (`ReorderableItemsViewController2`)**: its own `UILongPressGestureRecognizer` (private) calls
  `BeginInteractiveMovementForItem`. No lift (no scale, no shadow), no haptic, no VoiceOver action.
  `MoveItem` does `RemoveAt`/`Insert` on `ItemsSource as IList` with change observation off, so a
  read-only source throws and there is no way to hand the move to a command.
- **Android (`MauiRecyclerView`)**: a private `ItemTouchHelper` with long-press drag. The list is
  mutated on every step of the drag and `ReorderCompleted` fires on every step, not once on drop.
  The helper is private, so a handle cannot call `StartDrag` on it.
- **Windows**: `ListViewBase.CanReorderItems`/`CanDragItems`, ungrouped observable sources only.

None of the three has a handle, an edit mode, a haptic or a screen-reader action. So Spine owns the
gesture and the move itself and does not build on `CanReorderItems` (combining the two is documented
as unsupported, and `Reorder.Mode` ≠ `Off` turns MAUI's off).

### API (core package, `Plugin.Maui.Spine.Extensions`, next to `Tap` and `Haptics`)
- `Reorder.Mode` (`ReorderMode.Off | LongPress | Handle | Edit`) on any `CollectionView`, which
  includes `HeroCollectionView`.
- `Reorder.IsHandle` on any view inside the item template.
- `Reorder.IsEditing` on the list (`Edit` mode): handles are active and visible only while true.
- `Reorder.Command` (optional) receiving `ReorderMove(int From, int To, object Item)` once after the drop,
  for saving the order. Spine itself moves the item in `ItemsSource`, which must be an `IList`
  (an `ObservableCollection<T>` gets `Move`, so the list animates once).
- Ungrouped sources only in v1; header/footer and items of a `DataTemplateSelector` all move.

### Native mapping
Mapper entries appended to `CollectionViewHandler2` (iOS/Mac) and `CollectionViewHandler`
(Android/Windows), as `SafeAreaExtensions` already does, with a per-list state object like `TapState`.

- **iOS / Mac Catalyst.** Spike first between two mechanisms and keep one:
  1. `UICollectionViewDragDelegate`/`DropDelegate`: system lift, haptic and VoiceOver drag for free;
     the drop updates the data and MAUI's observable source animates the move. But the lift always
     needs the system long-press, so `Handle` would not start on touch.
  2. Interactive movement driven by our own recognizers (long-press on the item, a zero-delay press on
     the handle) plus a Spine-drawn lift (scale + shadow on the cell) and Spine haptics. Works for all
     modes, but `MoveItem` belongs to MAUI's controller, so the data path needs a way round it.
  Possibly 1 for `LongPress` and 2 for `Handle`/`Edit`; the spike decides and goes in Decisions.
  Check against the back-swipe pan (#267) so a lift never starts a pop.
- **Android.** Spine's own `ItemTouchHelper` with `IsLongPressDragEnabled` only in `LongPress` mode;
  a handle's `ACTION_DOWN` calls `StartDrag`. Elevation on lift comes from `ItemTouchHelper`.
  The data moves with the drag (as `ItemTouchHelper` expects); the command runs once on drop with
  the first and last index.
- **Windows.** `CanReorderItems`/`CanDragItems`/`AllowDrop` for `LongPress` and `Edit`; `Handle` is the
  whole item (the mouse drags immediately anyway). Documented as such.

### Haptics and accessibility
- Lift: `Haptic.Medium`; each slot passed: `Haptic.Selection`; drop: `Haptic.Light` (the #303
  vocabulary; no extra haptic where the platform already plays one).
- Every movable item gets "Move up" / "Move down" (and "Move to top/bottom") actions:
  `UIAccessibilityCustomAction` on iOS, `AccessibilityActionCompat` on Android, so reordering works
  without dragging. Strings registered like the other Spine strings (English + Swedish).

### Showcase
- New gallery page "Reorder" (`Pages/Reorder/`) with the four modes as choices, a grip icon from
  the set as handle, an Edit/Done page action for `Edit` mode, and the code on the page.
- Hero page: its Reorder option switches to `Reorder.Mode="LongPress"`.
- Docs: wiki page + `spine-controls` skill section.

### Steps
1. iOS spike (both mechanisms in the Showcase on the simulator and the iPhone) → decide.
2. Core API + iOS, then Android, then Windows.
3. Accessibility actions and haptics.
4. Showcase page, Hero page, docs, skill.

## Open Questions


## Changes

- `Reorder` (`Mode`, `IsEnabled`, `Command`, `IsHandle`), `ReorderMode` and `ReorderMove` in
  `src/Plugin.Maui.Spine/Extensions/Reorder.cs`, with a per-list `ReorderState` and a per-handle
  `ReorderHandleState` that follow their handlers like `TapState`.
- iOS / Mac Catalyst (`Extensions/Reorder.Apple.cs`): `UICollectionView` interactive movement driven by
  Spine's own long-press on the list and a zero-delay press on each handle; lift = scale 1.03 on the
  cell's content view (not with Reduce Motion) + shadow; the item keeps the spot it was grabbed by and
  moves along the list's axis only; auto-scroll near the top and bottom edges with a display link.
- Android (`Platforms/Android/Reorder.Android.cs`): Spine's own `ItemTouchHelper`; long-press drag only
  in `LongPress`, a handle's touch-down calls `StartDrag`; movement flags follow the layout; the lifted
  row scales up unless animations are off.
- Windows (`Platforms/Windows/Reorder.Windows.cs`): `CanReorderItems` follows the mode; the command from
  `DragItemsStarting` / `DragItemsCompleted`. Compiled on the Mac only (see Decisions), not run.
- Screen-reader actions Move up / Move down / Move to top / Move to bottom on each realized item and its
  handle (`UIAccessibilityCustomAction`, `ViewCompat.AddAccessibilityAction`), refreshed after every
  move, on cell reuse and when the app adds or removes items; a handle without a description reads
  "Reorder". Strings `Spine.Reorder.*` in English and Swedish.
- Haptics: Medium on lift (iOS; Android's `ItemTouchHelper` plays its own long-press), Selection on
  every new place, Light on the drop.
- Showcase: gallery page "Reorder" (`Pages/Reorder/`) with Long-press / Handle / Edit button, List / Grid,
  an Edit/Done page action that drives `Reorder.IsEnabled` and the last move; the Hero page's Reorder option uses `Reorder.Mode`.
- Docs: `docs/wiki/reorder.md`, README rows, the core package README, a section in
  `hero-collection-view.md`, and the `spine-controls` skill.

## Decisions

- Spine owns the gesture and the move instead of building on MAUI's `CanReorderItems` (reasons above).
- Spine moves the item and `Reorder.Command` is only told afterwards (Jonatan, 2026-10-05): the same
  behaviour on every platform and the least code in an app; a read-only source cannot be reordered.
  The command's argument is therefore `ReorderMove`, not a request.
- iOS uses interactive movement, not the drag/drop delegates: the delegates' lift needs the system
  long-press, so `Handle` could not start on touch, and one mechanism for all modes keeps one data path.
  MAUI's controller is the collection view's data source and its `MoveItem` moves the item in the source
  with change observation off, so Spine sets `CanReorderItems = true` (its `CanMoveItem` answer) and
  disables the plain `UILongPressGestureRecognizer` MAUI adds with it. Screen-reader moves call the same
  `MoveItem` and then `UICollectionView.MoveItem` to animate.
- Android uses MAUI's adapter (`IItemTouchHelperAdapter.OnItemMove`) for every move, so the data moves the
  way MAUI's own reordering does, header offset included. MAUI's own `ItemTouchHelper` is private, so
  Spine attaches its own and leaves `CanReorderItems` false.
- Android: no shadow on the lifted row. `ItemTouchHelper` raises the row's container, which has no
  outline of its own (the card's shape is drawn by the MAUI `Border` inside it), so only the scale shows.
- Handles attach to their list when their item joins it (`ChildAdded`), because the template builds a
  handle before its item has a parent. Spine owns the handle's `IsVisible`: shown in `Handle` while
  `IsEnabled`, hidden otherwise and in `LongPress` and `Off`.
- `ReorderMode.Edit` and `Reorder.IsEditing` replaced by `Reorder.IsEnabled` (default true) in every
  mode (Jonatan, 2026-10-05): Edit was only Handle plus an on/off switch, and the switch is useful in
  every mode, for example while a list loads. An edit button is now `Mode="Handle"` with `IsEnabled`
  bound to the edit state.
- The Showcase shows List and Grid as two lists over the same cards: on iOS a list does not change its
  `ItemsLayout` once shown (MAUI 10.0.50, `CollectionViewHandler2`). `HeaderBar.ScrollSource` follows the
  visible one so it starts below the header bar.
- Grip icon: `griphorizontal.svg` (Jonatan, 2026-10-05), on the handles and for the gallery row.
