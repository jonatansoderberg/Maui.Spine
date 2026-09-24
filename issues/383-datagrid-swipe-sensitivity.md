# Issue #383 — DataGrid: swipe actions start while scrolling and make the scroll jag

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/383
**Branch:** issue/383-datagrid-swipe-sensitivity
**Status:** Completed

## Plan

Reported on an iPhone 16 Pro: the DataGrid's swipes are too sensitive, and a scroll jags instead of running straight.

Each row with actions is a MAUI `SwipeView`, which picks the drag's direction from its first move:

- **iOS** (`MauiSwipeView`): its `UIPanGestureRecognizer` recognizes simultaneously with the list's pan, and the direction is worked out again on every move. A scroll with a little sideways drift starts a swipe and slides the row sideways and back. Past 15 % of the actions' width, MAUI also sets `ScrollEnabled = false` on the list in the middle of the scroll.
- **Android** (`MauiSwipeView.OnInterceptTouchEvent`): it takes the gesture on the first move where |dx| > |dy|, with no touch slop. It then asks the list not to intercept, so a drag that starts at a slight angle does not scroll the list at all.

Keep `SwipeView`, and decide the direction once, after a movement the size of a scroll's slop. Only a mostly sideways drag becomes a swipe.

## Changes

- `DataGrid.Swipe.cs` (new):
  - `IsSwipeDrag`: a swipe must be 1.5× more sideways than vertical, i.e. within about 34° of the horizontal.
  - **iOS / Mac Catalyst:** the swipe view's pan gets a `ShouldBegin` that is asked once, when the pan has moved about 10 points. It approves only a sideways drag, or any drag while the row is open. A target on the same pan sets `ScrollEnabled = false` on the list while a swipe runs and restores it when the swipe ends.
  - **Android:** rows with actions are wrapped in an internal `SwipeGate` (a `ContentView`) whose handler creates `SwipeGateViewGroup`, a public `ContentViewGroup` subclass. `MauiSwipeView` itself cannot be subclassed, because its `SetElement` is internal.
    - Until the finger has moved the system touch slop, the gate holds `MOVE` events back from the swipe view.
    - A sideways drag (or an open row) is handed to the swipe view, and the list is asked to keep out.
    - Otherwise the row gets a `CANCEL`, the rest of the drag is kept from it, and the swipe view's "don't intercept" request is not passed on, so the list takes the drag.
- `DataGridExtensions.UseDataGrid()` (new) registers the `SwipeGate` handler on Android. `build/Plugin.Maui.Spine.Controls.DataGrid.props` declares it as a `SpineModule`, so `UseSpine()` calls it. The csproj packs `build\**` into `build/` and `buildTransitive/`.
- Docs:
  - `data-grid.md`: Registration and Swipe actions sections.
  - The package README.
  - `packages.md`: the registration table and the build-assets table.
  - The `/spine-controls` and `/spine-setup` skills.

## Verified

The gestures were driven with `touch_path` on the iPhone 17 simulator and `input motionevent` on the Pixel 10 Pro emulator. A scratch harness counted `SwipeStarted` and `SwipeChanging` and recorded the largest offset.

| Gesture | iOS before | iOS after | Android before | Android after |
|---|---|---|---|---|
| Vertical scroll with a sideways wobble after it started | 2 swipes started, row slid 23 pt | no swipe, list scrolls | no swipe | no swipe |
| Vertical drag that starts 2 dp sideways | — | — | swipe started, **list did not scroll** | no swipe, list scrolls |
| Sideways swipe | opens | opens (Delete revealed) | opens | opens |

- iOS: after a swipe the list scrolls again (the lock is released), and dragging the open row back closes it.
- Android:
  - Dragging an open row back closes it, and a tap on a row still runs the row command.
  - A vertical drag at the top of the list starts pull-to-refresh and is not taken as a tap on the row under the finger.
- `Spine.Packages.slnf` Release (Android, iOS, Mac Catalyst): 0 warnings, 0 errors.

## Decisions

- **Keep `SwipeView`**, and decide the direction in front of it. A swipe implementation of our own would mean re-doing MAUI's reveal, thresholds and item rendering on every platform.
- **1.5 (≈34°).** A thumb scroll that arcs a little stays a scroll. A deliberate swipe, even a sloppy one, stays inside 34°. A drag whose first 10 points really are sideways (about 18° in the test) still swipes, as it would in a `UITableView`.
- **iOS keeps MAUI's own pan.** `ShouldBegin` and an extra target sit on the recognizer MAUI creates, so they need no handler and no registration.
- **Android needs a registration.** The gate has to be the swipe view's parent, with a platform view of our own, so it needs a handler. The DataGrid therefore becomes a Spine module like AnimatedLabel. Without `UseSpine()` or `UseDataGrid()` the `SwipeGate` falls back to MAUI's `ContentViewHandler`, and the grid behaves as it did before.
- **The list is held still during a swipe** on both platforms. Previously it kept scrolling until the row had moved 15 % of the actions' width.
