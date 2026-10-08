# Issue #308 — Feature idea: Background tasks — [BackgroundTask] over BGTaskScheduler and WorkManager

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/308
**Branch:** issue/308-feature-idea-background-tasks-backgroundtask-over
**Status:** Completed

## Plan

This plan follows the study `docs/proposals/spine-background-tasks.md` and the owner's decisions of 2026-09-30 recorded there: a separate package `Plugin.Maui.Spine.BackgroundTasks`, contracts in `Plugin.Maui.Spine.Common`, `[BackgroundTask]` discovered at run time, two fixed iOS identifiers multiplexed in C#, `JobScheduler` on Android, an in-process timer plus a catch-up run on Windows and Mac Catalyst, `processing` opt-in, `RequestAsync` runs at once, and `IntervalMinutes` instead of the issue's `Interval = "00:15"`. Step 1 of the study (`UIBackgroundModes`, #435) is already merged.

This issue delivers the study's steps 2–6 and the sample/wiki part of step 7. `IPushSender.RunTaskAsync` and `BGContinuedProcessingTask` stay out.

### 1. Contracts in `Plugin.Maui.Spine.Common/Core`
- `BackgroundTaskAttribute(name)`: `IntervalMinutes`, `RequiresNetwork`, `RequiresCharging`, `Long`, `Widgets`.
- `IBackgroundTask.RunAsync(BackgroundTaskRun, CancellationToken)`, `BackgroundTaskRun(Name, Trigger, LastCompleted)`, `BackgroundTaskTrigger { Scheduled, Requested, Push, CatchUp }`.
- `IBackgroundTasks`: `RunsWhileClosed`, `Names`, `RequestAsync(name)`, `RequestAsync<TTask>()`, `StatusOf(name)`, a `StatusChanged` event.
- `BackgroundTaskStatus` and `BackgroundTaskOutcome`.
- An internal `SpineBackgroundTaskRegistration` (visible to Widgets and BackgroundTasks) so a Spine package can add a built-in task without referencing the BackgroundTasks package.

### 2. The package `src/Plugin.Maui.Spine.BackgroundTasks`
- `UseSpineBackgroundTasks(options)` registered as a `<SpineModule>` (props), idempotent like `UseSpineWidgets`; options for the interval per task at run time.
- `BackgroundTaskRegistry`: scans `SpineOptions.Assemblies` like `WidgetRegistry`, plus the built-in registrations; warns for a `[BackgroundTask]` that is not an `IBackgroundTask`, duplicates, and a task whose constructor takes `INavigationService`.
- `BackgroundTaskPlanner` (no MAUI types, unit tested): which tasks are due, most overdue first, and the earliest next due time.
- The persisted schedule: one JSON document in `Preferences` through a source-generated `JsonSerializerContext`.
- The dispatcher (`BackgroundTaskService : IBackgroundTasks`): one DI scope per run, the thread pool, at most one run per name (a second request joins the run in flight), status persisted, widgets named in `Widgets` refreshed through `IWidgetService` after a completed run, catch-up at start and on return to the foreground.
- **iOS:** reads `<ApplicationId>.spine.refresh` / `.spine.processing` from `BGTaskSchedulerPermittedIdentifiers`, registers both in `FinishedLaunching`, runs due tasks in the launch handler until expiration, `SetTaskCompleted` exactly once, submits the next request at `DidEnterBackground` and after each run.
- **Android:** a `JobService` (`BIND_JOB_SERVICE`), one persisted periodic job per task with a stable id, network/charging constraints, rescheduled only when its parameters change, stale jobs cancelled; `RECEIVE_BOOT_COMPLETED` declared by the package.
- **Windows and Mac Catalyst:** a one-minute in-process timer while the app runs plus the catch-up runs; `RunsWhileClosed = false`.
- `build/Plugin.Maui.Spine.BackgroundTasks.targets`: `SpineBackgroundTasksEnabled` (default true) → `fetch` + `.spine.refresh`; `SpineBackgroundTasksProcessing` (default false) → `processing` + `.spine.processing`.

### 3. Info.plist: `BGTaskSchedulerPermittedIdentifiers`
- It is an array, so it has the same merge problem as `UIBackgroundModes` (#435). `Plugin.Maui.Spine.Common.targets` gets `<SpineBackgroundTaskIdentifier>` items, written into the same partial plist as the modes together with the app's own identifiers.
- Widgets stops writing the key in `HostManifest.plist` and contributes `.spine-widgets.refresh` as an item instead — only when the BackgroundTasks package is not in the build.

### 4. Widgets moves in (without a hard dependency)
- Widgets registers `spine.widgets` as a built-in task (the `IBackgroundRefreshHandler`, then `RefreshAllAsync`, every `BackgroundRefreshInterval`).
- With the BackgroundTasks package referenced: iOS has no `.spine-widgets.refresh` in the plist, so Widgets registers nothing itself; Android stops booking (and cancels) its alarm when `IBackgroundTasks` is registered.
- Without it: Widgets behaves exactly as before (the one-release alias in the decisions).

### 5. Showcase, wiki, skills, tests
- Showcase page `Pages/BackgroundTasks` with an entry in `SampleIndex`: the tasks, their status, Run now, and a sample task that refreshes the `sample` widget.
- `docs/wiki/background-tasks.md`, README tables, `docs/wiki/packages.md`, `docs/wiki/widgets.md`, the study's status, `spine-setup`/`spine-widgets` skills.
- `tests/Plugin.Maui.Spine.BackgroundTasks.Tests` for the planner and the schedule document.

## Open Questions

None blocking; decisions taken overnight are listed under Decisions for review.

## Changes

- **Common contracts** (`src/Plugin.Maui.Spine.Common/BackgroundTasks/`): `BackgroundTaskAttribute`, `IBackgroundTask`, `BackgroundTaskRun`, `BackgroundTaskTrigger`, `IBackgroundTasks` (`RunsWhileClosed`, `Names`, `RequestAsync(name)`, `RequestAsync(name, trigger)`, `RequestAsync<T>()`, `StatusOf`, `StatusChanged`), `BackgroundTaskStatus`, `BackgroundTaskOutcome`, and the internal `SpineBackgroundTaskRegistration` for built-in tasks. `PushKeys.Task` (`spine.task`).
- **New package `Plugin.Maui.Spine.BackgroundTasks`**: `UseSpineBackgroundTasks` (a `<SpineModule>`, idempotent), `SpineBackgroundTasksOptions` (`Interval(name, TimeSpan)`, `CatchUp`), `BackgroundTaskRegistry` (scan of `SpineOptions.Assemblies` + built-in registrations, warnings for non-`IBackgroundTask`, duplicates, bad attributes and `INavigationService` in a constructor), `BackgroundTaskPlanner`, `BackgroundTaskState` (JSON in `Preferences`, source-generated context), `BackgroundTaskService` (scope per run, thread pool, in-flight join per name, status, widget refresh after a completed run, offline skip for `RequiresNetwork`, due-ness re-checked before each task).
- **iOS**: two fixed identifiers read from `Info.plist`, registered once in `FinishedLaunching`; the launch handler books the next request before working, runs the due tasks (short ones on refresh, `Long` ones on processing, or all on refresh without a processing identifier), cancels on expiration and calls `SetTaskCompleted` exactly once (with a 2 s fallback for a task that ignores its token); booking at `DidEnterBackground`/`SceneDidEnterBackground` and after every run; catch-up at `OnActivated`/`SceneOnActivated`.
- **Android**: `SpineBackgroundJobService` (`BIND_JOB_SERVICE`), one persisted periodic job per task with an FNV job id, network/charging constraints, rebooked only when its parameters change, stale jobs cancelled; checked at `OnResume` (with the catch-up) and `OnStop`; `RECEIVE_BOOT_COMPLETED` and `ACCESS_NETWORK_STATE` declared by the package.
- **Windows and Mac Catalyst**: an `IMauiInitializeService` starts a loop: catch-up 5 s after launch, then a check every minute.
- **Build**: `build/Plugin.Maui.Spine.BackgroundTasks.props` (module) and `.targets` (`SpineBackgroundTasksEnabled` → `fetch` + `.spine.refresh`; `SpineBackgroundTasksProcessing` → `processing` + `.spine.processing`). `Plugin.Maui.Spine.Common.targets` now also writes `BGTaskSchedulerPermittedIdentifiers` from `<SpineBackgroundTaskIdentifier>` items merged with the app's own, in the same partial plist as `UIBackgroundModes`.
- **Widgets**: the identifier moved from `HostManifest.plist` (`spine-widgets-build.sh`, `--background-refresh` removed) to a `<SpineBackgroundTaskIdentifier>` item, left out when `SpineBackgroundTasksEnabled` is true. Widgets registers `spine.widgets` as a built-in task; on Android it stops booking (and cancels) its alarm and ignores an old one when `IBackgroundTasks` is registered. Without the BackgroundTasks package nothing changes.
- **Push**: `HandleInternallyAsync` runs the `spine.task` task (trigger `Push`) for silent and widget messages, before the widget refresh.
- **Showcase**: page *Background tasks* (`Pages/BackgroundTasks`), `BackgroundTasks/SampleSyncTask.cs` (`sample-sync`, every 15 min, rebuilds the `sample` widget) and `FlakyUploadTask` (on request, fails every other run); `SampleIndex`, `GlobalXmlns.cs`, csproj reference and targets import.
- **Push sample**: `Tasks/StampTask.cs` (`stamp`, runnable by `spine.task=stamp`), package reference and targets import; also the missing `Plugin.Maui.Spine.Extensions` XAML namespace in `GlobalXmlns.cs` (the sample did not build on master since `SafeArea.PageMargin` came into `LogPage`).
- **Tests**: `tests/Plugin.Maui.Spine.BackgroundTasks.Tests` (planner and stored schedule, 14 tests).
- **Docs**: `docs/wiki/background-tasks.md` with a screenshot, README tables (sixteen packages), `docs/wiki/packages.md`, `docs/wiki/widgets.md`, `docs/wiki/push-notifications.md`, the study's status, the package README and icon (`assets/icons/background-tasks.png`), skills `spine-setup`, `spine-widgets`, `spine-notifications`.

## Decisions

- **Widgets moves in without a hard dependency.** The study has Widgets reference BackgroundTasks; instead Widgets contributes `spine.widgets` through an internal `SpineBackgroundTaskRegistration` in Common, which only the BackgroundTasks package reads. Apps with Widgets alone keep exactly today's behaviour (this is also the decision's "one release as aliases"), and apps with both get one schedule. No build warning was added for `SpineWidgetsBackgroundRefresh`: it still means something without the new package.
- **`BGTaskSchedulerPermittedIdentifiers` goes through Common like `UIBackgroundModes`.** It is an array with the same merge problem (#435); otherwise Widgets' and BackgroundTasks' partial plists would erase each other's identifier.
- **Due = one interval after the last *start*,** whatever the outcome, so a failing task is retried on its schedule, not in a loop; a task that never ran is due at once (first launch runs everything once).
- **`RequestAsync` always runs in-process** (the decision for the foreground), also when called in the background (push). An Android expedited job for long work handed over from a push is a follow-up.
- **Android jobs run their task unconditionally** (the platform decided), while the catch-up re-checks due-ness before each task so a job that just ran is not repeated. JobScheduler runs a newly booked periodic job right away, so on first launch a task can run twice in a few seconds (catch-up, then job) — accepted.
- **Catch-up only on activation, not in `FinishedLaunching`:** a background launch (BGTask, widget button) must not start runs outside the BGTask's time budget.
- **Long tasks share one processing request**: network if any needs it, external power only if all do.
- **`RequiresNetwork` on iOS refresh / timers**: there is no constraint, so the dispatcher skips such a task while `Connectivity` says offline (it stays due). Requests run regardless.
- **Package declares `ACCESS_NETWORK_STATE`** on Android for that check (normal permission).
- **`IBackgroundTasks.RequestAsync(name, trigger)`** was added beside the study's API so Push can pass `Push` as the trigger.
- **`StatusChanged` event** added to `IBackgroundTasks` so a status page can follow runs.
- **The push sample's missing XAML namespace** was fixed here because it blocked building the sample; it is one line.
- **Icon**: the Scanner icon's corner recoloured teal with a clock glyph, generated from `assets/logo-src/scanner.png`.
