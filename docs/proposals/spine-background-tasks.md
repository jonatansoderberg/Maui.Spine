# Background tasks in Spine (study, rev 1)

**Status:** Study, with the owner's decisions from 2026-09-30 in the [Decisions](#decisions-2026-09-30) section. Steps 2–6 implemented in #308 (`Plugin.Maui.Spine.BackgroundTasks`, see `docs/wiki/background-tasks.md` and `issues/308-feature-idea-background-tasks-backgroundtask-over.md`); the device verification of step 7 and `IPushSender.RunTaskAsync` remain. Issue: [#308](https://github.com/jonatansoderberg/Maui.Spine/issues/308), prioritized in [#317](https://github.com/jonatansoderberg/Maui.Spine/issues/317) as P2 with the questions *the time of registration on iOS* and *Windows*.
**Question:** Can Spine offer attribute-discovered background tasks (`[BackgroundTask]` + `IBackgroundTask`) on top of BGTaskScheduler and Android's schedulers, so that widgets and Live Activities stay fresh without the app being in the foreground — and how do we deal with iOS requiring registration before the app has finished launching?
**Answer:** Yes, as a separate package `Plugin.Maui.Spine.BackgroundTasks`, with the attribute discovered at run time just like `[Widget]`. But **not with one iOS identifier per task**: iOS allows a single pending refresh request per app and requires every identifier in `Info.plist` to have a handler registered before `didFinishLaunching` returns. Spine therefore registers two fixed identifiers (`<ApplicationId>.spine.refresh` and `.spine.processing`) in MAUI's `FinishedLaunching` event and decides itself, in C#, which tasks are next in line. That way no build-time discovery is needed, the time of registration becomes Spine's responsibility instead of the app's, and Widgets' existing background run moves in as one task among others. Android: `JobScheduler` directly, not WorkManager. Windows and Mac Catalyst get no OS schedule in v1. There the tasks run only while the app is running, and that must be stated plainly.

---

## 1. The conclusion in short

| Question | Answer | Evidence |
|---|---|---|
| Does Spine register in time on iOS? | **Yes.** MAUI builds `MauiApp` in `willFinishLaunching` and raises the lifecycle event `FinishedLaunching` *inside* `didFinishLaunching`, before `return true`. Widgets already registers its `BGAppRefreshTask` there. | `MauiUIApplicationDelegate.cs` (dotnet/maui), `SpineWidgetsExtensions.Apple.cs:31-33` |
| One identifier per `[BackgroundTask]`? | **No.** Apple: *"There can be a total of 1 refresh task and 10 processing tasks scheduled at any time."* Two packages with a refresh identifier each push each other out. Spine multiplexes the tasks over one refresh identifier and one processing identifier. | The `submit(_:)` documentation, §4 |
| Is build-time discovery needed (source generator, `<SpineBackgroundTask>` items)? | **No**, precisely because the identifiers are fixed. The build step writes two strings and a background mode; everything else is reflection at startup, as for pages and widgets. | §5.3 |
| A separate package or in the core? | **A separate package**, with the contracts in `Plugin.Maui.Spine.Common` so that Push and Widgets can call them without a hard dependency (the same pattern as `IWidgetService`). Widgets takes a dependency on the package and hands over its BGTask code. | §5.1 |
| WorkManager on Android? | **No, `JobScheduler`.** It is part of Mono.Android, gives periodic runs at ≥ 15 min, network and charging constraints, survives a reboot and has expedited jobs from API 31. The WorkManager binding pulls in Room, Kotlin coroutines and Lifecycle 2.11 — exactly the packages whose versions Push has already had to force together — and #171 turned WorkManager down for the same reason. **The owner's decision**, see §11. | The nuspec for `Xamarin.AndroidX.Work.Runtime` 2.11.2.1, `Directory.Packages.props`, `issues/171-…md` |
| Windows? | **An in-process timer** while the app is running, plus a catch-up run at startup. `BackgroundTaskBuilder` in the Windows App SDK requires MSIX, and Spine's apps run with `WindowsPackageType=None`. | Microsoft's documentation (§3.4), `samples/*/…csproj` |
| Mac Catalyst? | **Same as Windows.** The API exists, but Apple DTS (Nov 2025): *"the BackgroundTask framework will not launch your app on macOS"*. | Apple Developer Forums 807388 |
| Does it keep widgets fresh? | **Better than today, never exactly.** iOS decides itself when a refresh runs; real time requires push (the #287 route). What the task solves is that data is fetched and the tree rebuilt *without* the app being opened. | §8, `docs/wiki/widgets.md` |

---

## 2. What Spine already has

Spine already has a background run, but only for widgets and only one:

| Part | Where | What it means here |
|---|---|---|
| `IBackgroundRefreshHandler` + `UseBackgroundRefresh<T>()` | `Common/Core/IBackgroundRefreshHandler.cs:15`, `SpineWidgetsOptions.cs:31,48` | The model for `IBackgroundTask`: one method, one token, resolved through DI on every run. Becomes an adapter (§8). |
| Registration on iOS | `SpineWidgetsExtensions.Apple.cs:21-25` reads the identifier from `Info.plist`, `:31-33` registers in `FinishedLaunching`, `:137-160` sets `ExpirationHandler` and schedules the next run *before* the work starts | The right order and the right time, and that is exactly the code that moves. |
| Scheduling on iOS | `:45-49` (`DidEnterBackground`), `:162-167` (`BGAppRefreshTaskRequest` with `EarliestBeginDate`) | The same pattern, but with the earliest due time across all tasks. |
| The build step, iOS | `spine-widgets-build.sh:264-265` writes `UIBackgroundModes: fetch` and `<ApplicationId>.spine-widgets.refresh` to `HostManifest.plist`, which becomes a `PartialAppManifest` (`Plugin.Maui.Spine.Widgets.targets:129`) | Occupies the app's only refresh slot. Two refresh identifiers cannot coexist (§4). |
| Android | `SpineBackgroundReceiver.cs:50-64`: an inexact alarm (`SetAndAllowWhileIdle`), scheduled at `OnStop` (`SpineWidgetsExtensions.Android.cs:39`) and after every run | Works, but see the two findings below. |
| Attribute discovery | `WidgetRegistry.cs:13-32` and `RegisterNavigables` (`MauiAppBuilderExtensions.cs:195`) scan `SpineOptions.Assemblies` with reflection | `BackgroundTaskRegistry` becomes a third scan of the same kind. |
| Module registration | `build/<Package>.props` with `<SpineModule>` → `SpineModules.g.cs` with `[ModuleInitializer]` → `UseSpine()` runs the modules last (`MauiAppBuilderExtensions.cs:157`, issue #355) | The package registers itself, and with it its lifecycle hooks, in time. |
| Early hooks in Push | `FinishedLaunching` (`SpinePushNotificationsExtensions.Apple.cs:22`), `OnApplicationCreate` (`…Android.cs:17`), `SpinePushNotifications.Install()` before `UIApplication.Main` (`SpinePushNotifications.Apple.cs:53`) | Shows how early Spine already reaches: Install is needed for delegate methods UIKit reads at assignment. BGTaskScheduler does not need to go that early. |
| Push that wakes work | `HandleInternallyAsync` rebuilds widgets for `PushKind.Widget` (`SpinePushNotificationsExtensions.cs:164-178`); FCM gives 20 s (`SpinePushNotificationsMessagingService.cs:43`) | A natural entry point for `spine.task` (§8). |
| Background launch through a widget button | #218: iOS launches the app's process in the background for `SpineWidgetIntent`. The cold start took ~4 s in the simulator | The same cold start is paid by every BGTask that launches a dead app. And a background launch is exactly the situation where late registration crashes (§4). |
| Shared plist/entitlements step | `Plugin.Maui.Spine.Common.targets:1-24`: `<SpineEntitlement>` items, one target writes the file | The pattern that is needed for `UIBackgroundModes` as well. |

**Two findings in the existing code** that an implementation has to deal with. Both are evidence from the source code, not verified in a build or on a device:

1. **`UIBackgroundModes` is written by two `PartialAppManifest` files and one of them wins.** Push writes `remote-notification` (`Plugin.Maui.Spine.PushNotifications.targets:76-77`), Widgets writes `fetch`. The SDK's `MergePartialPlistDictionary` (dotnet/macios, `CompileAppManifest.cs`) only merges *dictionaries*. An array is replaced. An app with both packages, such as the push sample, therefore gets only one of the modes. The one that loses `remote-notification` gets no silent pushes in the background, and the one that loses `fetch` never gets its `BGAppRefreshTask`. Check: `plutil -p` on the built push sample's `Info.plist`. Fix: `<SpineBackgroundMode>` items in `Common.targets`, where one target writes one plist, like `<SpineEntitlement>`.
2. **The Android run has no time limit and does not survive a reboot.** `SpineBackgroundReceiver` runs the handler with `CancellationToken.None` behind `goAsync()` (`:36-42`), but Android expects a broadcast to finish *"very quickly (under 10 seconds)"*. The alarm is scheduled only at `OnStop`, and alarms are cleared on reboot (as already noted in `spine-local-notifications.md` §6). After a reboot the background run therefore stands still until the app is opened.

---

## 3. The platforms' building blocks

### 3.1 iOS

- **`BGAppRefreshTask`**: short, *"small bits of information"*. Requires `UIBackgroundModes: fetch`. The budget is around 30 s. That is stated in Spine's own documentation (`IBackgroundRefreshHandler.cs`) and is generally accepted, but it is not in Apple's API reference.
- **`BGProcessingTask`**: minutes, but *"Processing tasks run only when the device is idle. The system terminates any background processing tasks running when the user starts using the device."* Requires `UIBackgroundModes: processing`. The request can require network (`RequiresNetworkConnectivity`) and charging (`RequiresExternalPower`).
- **`BGContinuedProcessingTask`** (iOS 26): started from the foreground *"in response to someone's action"* and continues in the background with progress shown in a system-drawn Live Activity. It is not scheduling, but it fits "export/upload now". Bound in Microsoft.iOS, but as `NoMacCatalyst`, even though Apple's reference lists Mac Catalyst 26.
- **Info.plist:** `BGTaskSchedulerPermittedIdentifiers`. *"Every identifier in the [list] requires a handler."*
- **Quotas:** one pending refresh request and ten processing requests per app. `EarliestBeginDate` is a floor, never a time: *"the system doesn't guarantee launching the task at the specified date, but only that it won't begin sooner."*
- **The binding:** `BackgroundTasks` is part of Microsoft.iOS, and Widgets already uses it. In macios `main`, `Submit(request, out error)` is marked obsolete from iOS 27, in favor of a variant with a completion handler. Spine calls the old one (`SpineWidgetsExtensions.Apple.cs:165`).

### 3.2 Mac Catalyst

The classes are available from Catalyst 13.1, but the system does not launch the app for them. Apple's DTS answered in November 2025: *"No. More specifically, the BackgroundTask framework will not launch your app on macOS, though I believe your tasks may run if your app is running already."* The recommendation there is a LaunchAgent through `SMAppService`, which is outside Spine's scope. The same model as on Windows therefore applies (§3.4).

### 3.3 Android

- **`JobScheduler`** (Mono.Android): `setPeriodic` with 15 minutes as the floor, network, charging and idle constraints, `setPersisted` (requires `RECEIVE_BOOT_COMPLETED`, which Push already declares in `Permissions.cs:7`) and `setExpedited` from API 31. The job runs in the app's process in a `JobService` (`BIND_JOB_SERVICE`). The exact time limit for jobs has not been checked for this study.
- **WorkManager** (`Xamarin.AndroidX.Work.Runtime` 2.11.2.1 for `net10.0-android36.0`): the same 15-minute floor. On top of that come unique queues, backoff, expedited work with a fallback (`OutOfQuotaPolicy`, and `getForegroundInfo` is required on Android 11 and older), 10 minutes per worker and *"long-running"* through `setForeground`. From Android 16, however, long-running workers can *"exhaust your app's job quota"*. It persists to its own SQLite database and reschedules after a reboot.
- **The process:** `MauiApplication.OnCreate` builds `MauiApp` and creates `IApplication` (the app's `App`) before a service or receiver runs (dotnet/maui `MauiApplication.cs`). DI is therefore available when a job runs, but the app's `App` constructor also runs on every background launch.

### 3.4 Windows

`Microsoft.Windows.ApplicationModel.Background.BackgroundTaskBuilder` in the Windows App SDK registers a COM class that `backgroundtaskhost.exe` activates, with triggers and conditions (`InternetAvailable` among others). Microsoft's own documentation says: *"Background tasks using the Windows App SDK `BackgroundTaskBuilder` require your app to be packaged with MSIX."* Without MSIX, Microsoft points to Task Scheduler. That would launch the whole MAUI app with a window, and it requires a headless startup path that Spine does not have. Spine's samples run with `WindowsPackageType=None`. Which Windows App SDK version introduced the class has not been checked.

---

## 4. The time of registration on iOS

**The requirement.** *"Registration of all launch handlers must be complete before the end of `applicationDidFinishLaunching(_:)`."* Violating the requirement gives `NSInternalInconsistencyException: All launch handlers must be registered before application finishes launching`, and that is a crash, not a warning. Registering twice is just as bad: *"The system kills the app on the second registration of the same task identifier."*

**Why it matters in Spine in particular.** The crash only shows when the app is launched *in the background*. In Apple forum thread 775182 (iOS 18.4) it came from SwiftUI's `.backgroundTask`, which registered too late when the app was launched by a widget, a shortcut or Control Center. Apple's advice was to register in `didFinishLaunchingWithOptions`. Spine's widget buttons launch the app in the background (#218), so this is a scenario that actually occurs in Spine apps.

**Where MAUI puts us.** `MauiUIApplicationDelegate.WillFinishLaunching` calls `CreateMauiApp()`, and with it `UseSpine()` and the modules. `FinishedLaunching` sets the application handler, creates the window *if the app has no scene manifest*, raises the `iOSLifecycle.FinishedLaunching` events and only then returns `true`. A registration in `ios.FinishedLaunching(...)` that a package has added through `ConfigureLifecycleEvents` therefore happens in time. Widgets already does this. This is read from MAUI's source code on `main`, not stepped through in a debugger.

**What is *not* in time**, and therefore must not occur:

- Registration when `IBackgroundTasks` is resolved for the first time. The DI resolution can happen long after startup.
- Registration from `App.CreateWindow`, `OnStart` or a page's `OnAppearing`. With a scene manifest the window is created in the scene's `WillConnect`, after `didFinishLaunching`.
- Registration that depends on the app itself having called a `UseXxx()`. The module does it through `UseSpine`, and an app without `UseSpine` has to call `UseSpineBackgroundTasks()` in `MauiProgram`, which still happens in `willFinishLaunching`.

**Why fixed identifiers solve the rest.** The identifiers have to be in `Info.plist` at build time, but `[BackgroundTask]` is discovered at run time. With one identifier per task the build step would need to know about the tasks. That requires either the double declaration widgets have (`<SpineWidget>` in the csproj *and* `[Widget]` in code, `WidgetAttribute.cs:4-6`) or a source generator. With two fixed identifiers it is enough for the build step to know that the package is referenced. `FinishedLaunching` always registers exactly the two that are in the plist file, so *"every identifier requires a handler"* holds by construction. The quota of one refresh request forces the multiplexing anyway.

**Consequence for Widgets.** `…spine-widgets.refresh` and Spine's new refresh identifier cannot coexist. Each would find the other occupying the slot, and `Submit` would fail with *too many pending*. The background package therefore has to own *the only* registration, and Widgets becomes a client (§8). During the transition the two packages must never register the same identifier, because that kills the app.

---

## 5. Alternatives and trade-offs

### 5.1 Placement

| | A separate package (proposal) | In the core `Plugin.Maui.Spine` | Stays in Widgets |
|---|---|---|---|
| Who pays | Only apps that reference the package get the `fetch` mode and a new manifest element | Every Spine app, or an opt-in flag in the core's targets | Only widget apps |
| Push/Widgets reach it | Through `Common` contracts and `GetService`, like `IWidgetService` today | Directly | Push reaches it through `IWidgetService`, but an app without widgets cannot use it |
| Orientera "prefetch overnight" (no widget) | Yes | Yes | No |
| Risk | Widgets gets one more dependency | The core gets plist and manifest plumbing it does not have today | The wrong home |

A separate package, with `Widgets → BackgroundTasks → Common`. The core is a reasonable alternative if the owner would rather avoid one more package: on Android the addition is small, because `JobScheduler` pulls in no dependencies.

### 5.2 Attribute or builder API

The attribute follows `[Widget]`, `[NavigableRegion]` and `[NavigableTab]`: identity and default policy are on the class, and discovery goes through `SpineOptions.Assemblies`. A pure builder API (`o.AddTask<T>("name", every: …)`) would be the only thing in Spine that registers a type by hand. **Attributes for identity and defaults, options for what changes at run time**, for example the interval from a user setting.

The issue's `Interval = "00:15"` does not work as written. An attribute cannot take a `TimeSpan`, and `"00:15"` can be read as both 15 minutes and 15 seconds. The proposal is `IntervalMinutes = 15`.

### 5.3 One identifier per task or multiplexing

With one identifier per task iOS would decide the order, but the quota of one refresh request makes that impossible for more than one short task, and every task would require build-time knowledge. With multiplexing Spine decides: on every refresh the tasks that are due run, the most overdue first, until time runs out. The price is that a slow task can eat the budget of the others. Tasks marked `Long` therefore go to the processing identifier instead.

### 5.4 Android: alarm, JobScheduler or WorkManager

| | Alarm (today) | JobScheduler (proposal) | WorkManager |
|---|---|---|---|
| Constraints (network, charging) | No | Yes | Yes |
| Survives a reboot | No, requires its own boot receiver | `setPersisted` | Yes |
| Time per run | Under ~10 s (`goAsync`) | The job's limit (not checked) | 10 min, longer through `setForeground` |
| On request, quickly | No | `setExpedited` (API 31+) | `setExpedited` with a fallback |
| Dependencies | None | None | Work.Runtime → Room, Kotlin coroutines, Lifecycle 2.11, Startup |

JobScheduler provides what is missing without reopening the version fight in `Directory.Packages.props` (the Push block) once more. WorkManager is the better choice if Spine wants retries/backoff and Android 16 quota handling "for free". The choice is therefore a decision for the owner (§11).

---

## 6. Proposed API surface

The contracts live in `Plugin.Maui.Spine.Common` (net10.0, without MAUI), where `IWidgetService` also lives:

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BackgroundTaskAttribute(string name) : Attribute
{
    /// <summary>Stable name; also what <see cref="IBackgroundTasks.RequestAsync"/> and a push's <c>spine.task</c> use.</summary>
    public string Name { get; } = name;

    /// <summary>Minutes between scheduled runs. 0 runs it only when requested. Android's floor is 15.</summary>
    public int IntervalMinutes { get; set; }

    public bool RequiresNetwork { get; set; }
    public bool RequiresCharging { get; set; }

    /// <summary>Minutes of work: a BGProcessingTask on iOS (only while the device is idle), a normal job on Android.</summary>
    public bool Long { get; set; }

    /// <summary>Widget kinds rebuilt after a run that completed.</summary>
    public string[] Widgets { get; set; } = [];
}

public interface IBackgroundTask
{
    Task RunAsync(BackgroundTaskRun run, CancellationToken cancellationToken);
}

public sealed record BackgroundTaskRun(string Name, BackgroundTaskTrigger Trigger, DateTimeOffset? LastCompleted);

public enum BackgroundTaskTrigger { Scheduled, Requested, Push, CatchUp }

public interface IBackgroundTasks
{
    /// <summary>Whether the platform runs tasks while the app is closed. False on Windows and Mac Catalyst.</summary>
    bool RunsWhileClosed { get; }

    IReadOnlyList<string> Names { get; }

    /// <summary>Runs <paramref name="name"/> now when the app is running; otherwise asks the platform to run it soon.</summary>
    Task RequestAsync(string name, CancellationToken cancellationToken = default);

    Task RequestAsync<TTask>(CancellationToken cancellationToken = default) where TTask : IBackgroundTask;

    /// <summary>When it last ran and how it went: the answer to "why is my widget old?".</summary>
    BackgroundTaskStatus StatusOf(string name);
}
```

In the app:

```csharp
[BackgroundTask("standings", IntervalMinutes = 30, RequiresNetwork = true, Widgets = ["team"])]
public sealed class StandingsTask(IStandingsApi _api, StandingsStore _store) : IBackgroundTask
{
    public async Task RunAsync(BackgroundTaskRun run, CancellationToken cancellationToken) =>
        await _store.SaveAsync(await _api.FetchAsync(cancellationToken), cancellationToken);
}

builder.UseSpineBackgroundTasks(o => o.Interval("standings", settings.RefreshEvery)); // optional, TimeSpan.Zero turns it off
```

**The build step.** `SpineBackgroundTasksEnabled` is `true` by default and gives a `fetch` mode and `<ApplicationId>.spine.refresh`. `SpineBackgroundTasksProcessing` is `false` by default and adds `processing` and `.spine.processing`. Processing is opt-in because a background mode the app does not use should not be declared. At startup Spine logs a warning for a `Long` task without a processing identifier and runs it as a refresh, in the same way that Widgets validates `[Widget]` against `<SpineWidget>`. On Android, `JobService` and `RECEIVE_BOOT_COMPLETED` are written to the manifest overlay.

**The schedule** is persisted per task (last started, last completed, result, earliest next) in `Preferences`, through a `JsonSerializerContext` as in local notifications. That is what `StatusOf` reads, and what the multiplexing uses on iOS to pick tasks.

---

## 7. DI scope and what a task may touch

- **One scope per run.** `await using var scope = services.CreateAsyncScope()`, then `ActivatorUtilities.CreateInstance(scope.ServiceProvider, type)`, and the scope is disposed after the run. Widgets today creates handlers from the root provider (`SpineWidgetsExtensions.cs:47,61,89`), which means transient `IDisposable` instances live until the process dies.
- **The thread pool, not the main thread.** Widget buttons run on the main thread (`:87`) because they may touch the app's state. A background task should not do that, and the main thread may be busy building a window that nobody sees.
- **Allowed:** `HttpClient`, files, `Preferences`, databases, `IWidgetService`, `ILiveActivityService.UpdateAsync` and `ILocalNotificationService.SyncAsync`. The last one is what Orientera needs: replanning the notifications after a sync.
- **Not allowed:** `INavigationService`, pages, dialogs and permission prompts (`RequestPermissionAsync` shows a system dialog). That also applies to anything that assumes a visible window. Spine cannot prevent it in the type system. The dispatcher therefore logs a warning if a task resolves `INavigationService` in its scope, and the wiki says so in the first paragraph.
- **Concurrency:** at most one run per name, with one lock per task. A `RequestAsync` in the foreground and a BGTask launch must not run the task twice at once.
- **Time limit:** the token that is passed in is cancelled by `ExpirationHandler` on iOS and by `onStopJob` on Android. A task that ignores the token gets killed, and Spine then calls `SetTaskCompleted(false)` or `jobFinished(…, true)`, exactly once.
- **Cold start:** a background launch runs `CreateMauiApp`, the app's `App` constructor and, on iOS without a scene manifest, `CreatePlatformWindow` as well. #218 measured ~4 s in the simulator. That fits in the budget but must be in the wiki: heavy startup work in `App` steals time from the task.

---

## 8. Connection to widgets and push

This connection is what makes the package worth building (#317: *"background tasks are what keep widget timelines fresh"*). The WidgetKit extension runs no .NET, and a `Refresh(after)` on iOS only re-reads the JSON file or fetches a `RemoteSource`. New data only comes into being when the app's process runs. The task is that process.

- **`Widgets = ["team"]`** on the attribute: after a run that completed, the dispatcher calls `IWidgetService.RefreshAsync(kind)` through `GetService`. On iOS that writes the tree and calls `reloadTimelines` through the bridge, and on Android `AppWidgetManager` through `RemoteViewsRenderer`. A task can also call the service itself.
- **Widgets' own background run becomes a built-in task**, `spine.widgets`: first `IBackgroundRefreshHandler` if there is one, then `RefreshAllAsync`, with the interval `BackgroundRefreshInterval`. The public API is unchanged. `SpineWidgetsBackgroundRefresh` and `.spine-widgets.refresh` are retired, and Widgets stops registering anything itself.
- **Push:** a new key `spine.task=<name>` on `Silent` and `Widget` messages. `HandleInternallyAsync` calls `IBackgroundTasks.RequestAsync` with `Trigger = Push`. On iOS the task runs immediately within the silent push's window (~30 s). It cannot run longer than that, and there is no iOS route for handing long work over from a push. On Android it can be queued as an expedited job that lives on after FCM's 20 s. The issue's use case *"offload long work from the notification handler"* can therefore only be met on Android.
- **Expectations per app.** Puckkoll every 15 minutes is a wish, not a promise. The real-time part comes from push, as #287 already showed during a game. Almanacka's pictures "at midnight" are most safely made in advance, as future timeline entries and rotating assets. A task that is meant to run exactly at midnight will not do so on iOS.

---

## 9. Platforms

| | Scheduled | On request | From push | Building block | Constraints |
|---|---|---|---|---|---|
| iOS | Yes, when the system wants; multiplexed | Immediately in the foreground, otherwise as `EarliestBeginDate = now` with no guarantee | Within the push's ~30 s | `BGAppRefreshTask`, `BGProcessingTask` | Network and charging only for `Long` |
| Mac Catalyst | Only while the app is running | Immediately if the app is running | As iOS if the app is running | In-process timer | No |
| Android | Yes, ≥ 15 min | Expedited (API 31+) | Expedited job | `JobScheduler` | Network, charging |
| Windows | Only while the app is running | Immediately | Push does not exist on Windows in Spine | In-process `PeriodicTimer` | No |

All platforms get a **catch-up run at startup and on return to the foreground**: tasks that are due run with `Trigger = CatchUp`. On Windows and Catalyst that is the main route, on iOS and Android a safety net.

---

## 10. What cannot be done, and what is not verified

The study was done in a Linux container, without a Mac, without a device and without Windows. **Nothing has been tried.** Everything below comes from documentation and source code.

1. **iOS timing belongs to the system.** There is no run every 15 minutes, and none at all if the user has force-quit the app or turned off Background App Refresh. That goes in the first paragraph of the wiki, not in a footnote.
2. **The multiplexing budget.** That all due refresh tasks fit in ~30 s is an assumption. How often iOS actually gives a refresh to an app whose *only* identifier carries several tasks has not been measured.
3. **The time of registration** in §4 is read from MAUI's source code on `main`, not stepped through. It must be verified with a background launch through a widget button *and* through `_simulateLaunchForTaskWithIdentifier:` on a physical device. BGTask launches cannot be tried in the simulator (`docs/wiki/widgets.md`).
4. **The `UIBackgroundModes` merge** (§2, finding 1) is a conclusion from the SDK code. It must be confirmed with `plutil -p` on the push sample's build before anything else is done.
5. **Mac Catalyst** rests on a DTS answer in a forum, not on documentation. If Apple changes the behavior, only `RunsWhileClosed` changes.
6. **JobScheduler details**: the time limit per job, which constraints are allowed with `setExpedited` and the quota behavior in Android 16 have not been checked. The WorkManager figures (10 min, 15 min) have.
7. **Android cold start:** can a job start before `MauiApplication.OnCreate` has finished? `JobService.onStartJob` runs on the main thread after `Application.onCreate` according to Android's model, but that has not been tried with MAUI. The dispatcher should wait for `IPlatformApplication.Current` instead of assuming it exists.
8. **Protected files on iOS**: a task that runs while the device is locked cannot read files with `NSFileProtectionComplete`. What MAUI's `Preferences` and `SecureStorage` use by default has not been checked.
9. **`BGContinuedProcessingTask`** is included as a possible v2. The binding and Apple's documentation disagree about Catalyst.

---

## 11. Delivery plan

**Decisions needed from the owner before step 2:**
- A separate package or the core (§5.1). Proposal: a separate package.
- JobScheduler or WorkManager (§5.4). Proposal: JobScheduler, in line with the decision in #171.
- May Widgets break with `SpineWidgetsBackgroundRefresh` and `.spine-widgets.refresh` in a minor version, or should they stay for one release as aliases?
- Should `RequestAsync` in the foreground run immediately (proposal) or always go through the platform?

**Steps:**
1. **Fix `UIBackgroundModes`** regardless of the rest: `<SpineBackgroundMode>` in `Common.targets`, where Push and Widgets contribute items. Verify with `plutil -p` on the push sample.
2. **Contracts and dispatcher**: the `Common` types, `BackgroundTaskRegistry`, the persisted schedule, one scope per run, locks, `StatusOf`, the catch-up run and the timer for Windows and Catalyst. Everything except the platform part can be unit tested on net10.0.
3. **iOS**: two fixed identifiers registered in `FinishedLaunching`, multiplexing, `ExpirationHandler`, scheduling at `DidEnterBackground` and after every run, and the plist keys through step 1.
4. **Android**: `JobService`, one job per task (stable id, like `AndroidNotifications.StableId`), constraints, `setPersisted` and an expedited job on request.
5. **Widgets moves in**: `spine.widgets` as a built-in task, `IBackgroundRefreshHandler` as an adapter, `SpineBackgroundReceiver` and the BGTask code removed, and `Widgets = [...]` on the attribute.
6. **Push**: `spine.task` in `HandleInternallyAsync`, and eventually `IPushSender.RunTaskAsync` in `Plugin.Maui.Spine.Server`.
7. **Sample, wiki, skill and device**: a task in the push sample that writes a timestamp to the widget. On iPhone through LLDB `_simulateLaunchForTaskWithIdentifier:` and `_simulateExpirationForTaskWithIdentifier:`, on Android through `adb shell cmd jobscheduler run -f <package> <id>`. After that, 24 hours in Puckkoll with `StatusOf` logged, to get real figures for how often iOS actually runs.

---

## Decisions (2026-09-30)

Jonatan went through the study's questions on 2026-09-30 and followed the recommendations. Rows marked **Proposal** had no recommendation in the study; they carry a proposal with reasons, which applies until he says otherwise.

- **Placement.** A separate package `Plugin.Maui.Spine.BackgroundTasks`, with the contracts in Common (§5.1).
- **Android.** `JobScheduler`, not WorkManager, in line with #171 (§5.4).
- **Widgets' old surfaces** — **Proposal.** `SpineWidgetsBackgroundRefresh` and `.spine-widgets.refresh` stay for one release as aliases, with a build warning that points to the new ones, and are removed in the version after. The packages are on nuget.org, and Puckkoll, Almanacka and Orientera follow them through `SpineVersion`; an identifier that disappears in a minor version silently stops running in the background instead of giving a build error.
- **`RequestAsync` in the foreground.** Runs the task immediately, without a detour through the platform.
- **iOS identifiers.** Two fixed ones (`.spine.refresh` and `.spine.processing`) that Spine multiplexes, not one per task (§5.3).
- **Declaration.** Attributes for identity and defaults, options for changes at run time. `IntervalMinutes = 15` instead of the issue's `Interval = "00:15"` (§5.2).
- **Windows and Mac Catalyst.** An in-process timer and a catch-up run at startup, visible as `RunsWhileClosed = false`.
- **The `processing` mode.** Opt-in: `SpineBackgroundTasksProcessing=false` by default.
- **`UIBackgroundModes`.** Fixed first, regardless of the rest (step 1). The bug is confirmed on a build of the push sample on 2026-09-30: [#435](https://github.com/jonatansoderberg/Maui.Spine/issues/435). Fixed by #435 with `<SpineBackgroundMode>` items in `Plugin.Maui.Spine.Common.targets`; a background-tasks package contributes `processing` the same way.
- **Push.** The key `spine.task=<name>` triggers a task; `IPushSender.RunTaskAsync` comes later.
- **`BGContinuedProcessingTask`.** Not in v1.

---

## 12. References

Read in the repo: `SpineWidgetsExtensions.Apple.cs`, `SpineWidgetsExtensions.cs`, `SpineBackgroundReceiver.cs`, `SpineWidgetsExtensions.Android.cs`, `WidgetRegistry.cs`, `Plugin.Maui.Spine.Widgets.targets`, `spine-widgets-build.sh`, `Plugin.Maui.Spine.PushNotifications.targets`, `Plugin.Maui.Spine.Common.targets`, `Plugin.Maui.Spine.targets`, the push package's lifecycle files, `MauiAppBuilderExtensions.cs`, `docs/wiki/widgets.md`, `docs/wiki/push-notifications.md`, `issues/171-…`, `issues/218-…`, `issues/287-…`, `issues/355-…`.

- Apple, `register(forTaskWithIdentifier:using:launchHandler:)`: https://developer.apple.com/documentation/backgroundtasks/bgtaskscheduler/register(fortaskwithidentifier:using:launchhandler:)
- Apple, `submit(_:)` (1 refresh + 10 processing): https://developer.apple.com/documentation/backgroundtasks/bgtaskscheduler/submit(_:)
- Apple, `BGAppRefreshTask` / `BGProcessingTask`: https://developer.apple.com/documentation/backgroundtasks/bgapprefreshtask, https://developer.apple.com/documentation/backgroundtasks/bgprocessingtask
- Apple, *Using background tasks to update your app*: https://developer.apple.com/documentation/uikit/using-background-tasks-to-update-your-app
- Apple, `earliestBeginDate`: https://developer.apple.com/documentation/backgroundtasks/bgtaskrequest/earliestbegindate
- Apple, *Performing long-running tasks on iOS and iPadOS* (`BGContinuedProcessingTask`): https://developer.apple.com/documentation/backgroundtasks/performing-long-running-tasks-on-ios-and-ipados
- Apple Developer Forums, Mac Catalyst and BackgroundTasks (DTS, Nov 2025): https://developer.apple.com/forums/thread/807388
- Apple Developer Forums, the crash on late registration (iOS 18.4): https://developer.apple.com/forums/thread/775182
- dotnet/macios, the BackgroundTasks binding: https://github.com/dotnet/macios/blob/main/src/backgroundtasks.cs
- dotnet/macios, `CompileAppManifest.cs` (merging of partial plist files): https://github.com/dotnet/macios/blob/main/msbuild/Xamarin.MacDev.Tasks/Tasks/CompileAppManifest.cs
- dotnet/maui, `MauiUIApplicationDelegate.cs`: https://github.com/dotnet/maui/blob/main/src/Core/src/Platform/iOS/MauiUIApplicationDelegate.cs
- dotnet/maui, `MauiApplication.cs` (Android): https://github.com/dotnet/maui/blob/main/src/Core/src/Platform/Android/MauiApplication.cs
- Android, WorkManager *Define work*: https://developer.android.com/develop/background-work/background-tasks/persistent/getting-started/define-work
- Android, *Support for long-running workers*: https://developer.android.com/develop/background-work/background-tasks/persistent/how-to/long-running
- Android, *Persistent work*: https://developer.android.com/develop/background-work/background-tasks/persistent
- Android, *Broadcasts* (`goAsync`, under 10 s): https://developer.android.com/develop/background-work/background-tasks/broadcasts
- Android, `JobInfo.Builder`: https://developer.android.com/reference/android/app/job/JobInfo.Builder
- NuGet, `Xamarin.AndroidX.Work.Runtime` 2.11.2.1 (nuspec dependencies): https://www.nuget.org/packages/Xamarin.AndroidX.Work.Runtime
- Microsoft, *Using background tasks in Windows apps* (read through the source in MicrosoftDocs/windows-dev-docs, because learn.microsoft.com was blocked from the container): https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/background-tasks
