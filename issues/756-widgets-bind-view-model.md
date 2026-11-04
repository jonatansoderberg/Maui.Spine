# Issue #756 — Widgets: Bind(viewModel) for Live Activities

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/756
**Branch:** issue/756-widgets-bind-view-model
**Status:** Completed

Step W13 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md).

## Plan

`Build(model)` is a snapshot; `Bind` keeps a running activity in step with a view model, one update per burst, built and sent off the caller's thread.

## Changes

- `Plugin.Maui.Spine.Common`: `LiveActivityBinding` (`Updates`, `Failed`, `Refresh`, `FlushAsync`, `Dispose`), `LiveActivityBindingOptions` (`Interval`, `Time`), `LiveActivityBindings.Bind` on a `LiveActivity` and `StartBoundAsync` on `ILiveActivityService`.
- Generator: for a `LiveActivity` whose model is `INotifyPropertyChanged`, `Bind(activity, m, …)` and `StartAsync(activities, kind, m, …)`, passing the model's first-level properties that announce changes and, with `{Loc}`, the strings.
- Tests: `BindingTests` (a burst is one update with the last values, an unseen change sends nothing, lists and replaced objects, flush and dispose, an ended activity, a failure then a success, start, only announcing models get `Bind`) on a fake clock.
- Docs: `widget-layout-files.md` "Following a view model"; the plan's W13 row and note.

## Decisions

- **Throttle, not debounce:** the first change opens the window, so a model that changes every 200 ms still updates once a second instead of never.
- **The same JSON is not sent twice**, and the layout `StartAsync` started with counts as sent.
- **First-level sources only**, re-read after every build; a deeper object has to raise a change on its parent.
- **Dispose drops a pending update** rather than sending from inside `Dispose`; `FlushAsync` is the explicit way.

## Verification

- `Plugin.Maui.Spine.Layouts.Tests`: 49 passed.
- Not run on a device: the binding sends through `LiveActivity.UpdateAsync`, the path every update already takes.
