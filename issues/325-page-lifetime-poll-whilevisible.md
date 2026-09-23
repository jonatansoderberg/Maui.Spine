# Issue #325 — Page lifetime: cancellation token, Poll, WhileVisible and UI-thread service events

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/325
**Branch:** issue/325-page-lifetime-poll-whilevisible
**Status:** Completed
**Stage:** 2 of the app-review plan (#332)

## Plan

### Gap
Every live page writes the same bookkeeping: a timer in `Task.Run` with its own `CancellationTokenSource`, cancelled in `OnDisappearingAsync`; subscribe in appear, unsubscribe in disappear, `MainThread.BeginInvokeOnMainThread` around the handler because Spine's service events arrive on whatever thread the platform used; a manual `Check()` on resume. None of it pauses when the app goes to the background. Spine already owns appear, disappear and resume, so the bookkeeping belongs in `ViewModelBase`.

### Design
On `ViewModelBase`:
- **`PageLifetime`** (`CancellationToken`): a fresh token per appearance, cancelled when the page disappears (region pop, sheet close, tab switch away). Before the first appearance and after a disappearance it is a cancelled token, so a stray `await` with it ends at once instead of running for a hidden page.
- **`Poll(TimeSpan interval, Func<CancellationToken, Task> work)`**: registers work that runs on the UI thread when the page appears, then every `interval`; pauses when the page disappears or the window is deactivated (background, notification shade, another window), and runs again at once when the page reappears or the window is activated. One registration lives for the view model's lifetime, so a singleton tab page polls every time it is shown. An exception in `work` is logged and the loop continues.
- **`WhileVisible`**: subscribes when the page appears and unsubscribes when it disappears, with the handler marshalled to the UI thread. Three shapes cover Spine's own events and MAUI's: `Action`, `Action<T>` and `EventHandler<T>`.
- **Suspend/resume**: `SpineApplication.HookResumed` already tracks `Deactivated`/`Activated`; it now also tells the shown view models to pause and resume their polls, before `OnResumedAsync` runs.
- **Service events on the UI thread**: `ILiveActivityService.ActivitiesChanged` / `ActivityEnded`, `IWidgetService.PushTokenChanged` and `IPushNotificationService.RegistrationChanged` are raised through `MainThread` when not already there, and their docs say so. Handlers that dispatched themselves keep working.

### Steps
1. `ViewModelBase`: lifetime token, poll registrations and runner, `WhileVisible` registrations, internal `SendSuspended`/`SendResumed`.
2. `SpineApplication.HookResumed`: suspend on deactivation, resume before `OnResumedAsync`.
3. Service events marshalled in `LiveActivityService`, `WidgetService`, `PushNotificationService`; interface docs.
4. Push sample: `LiveActivityPage` uses `WhileVisible` instead of its subscribe/unsubscribe pair.
5. Sample: `Pages/Lifetime/LifetimePage`: a 1-second `Poll` counter with a "paused" indicator, a fake download cancelled by `PageLifetime` when leaving, a `WhileVisible` on `Connectivity.ConnectivityChanged`.
6. Docs: `regions.md` lifecycle section and `page-pattern.md`; `/spine-page` skill.
7. Build iOS, Android, Mac Catalyst; verify on the simulator and emulator with logging that the poll stops when the app is backgrounded and runs at once on return.

## Open Questions

None. Decided: `PageLifetime` does not cancel on backgrounding, only on disappearing, because the page is still the one on screen and an in-flight load should complete for it; `Poll` is what pauses.

## Changes

- `Core/ViewModelBase.cs`: `PageLifetime` (new token per appearance, cancelled on disappearance, pre-cancelled otherwise), `Poll(interval, work)` with a `PollRegistration` loop on the UI thread (`PeriodicTimer`, immediate first run, exceptions logged), `WhileVisible` for `Action`, `Action<T>`, `EventHandler<T>` and a raw pair, internal `SendSuspended`/`SendResumed`. `SendAppearingAsync`/`SendDisappearingAsync`/`ForgetAppearance` begin and end the lifetime.
- `Core/SpineApplication.cs`: `HookResumed` pauses polls on `Deactivated` and restarts them on `Activated` before `OnResumedAsync`.
- `LiveActivityService` (`ActivitiesChanged`, `ActivityEnded`), `WidgetService.PushTokenChanged`, `PushNotificationService.RegistrationChanged` raised on the UI thread; `ILiveActivityService` docs updated.
- Push sample: `LiveActivityPage` uses `WhileVisible` and drops its subscribe/unsubscribe pair and its own dispatch.
- Sample: `Pages/Lifetime/LifetimePage` (1-second `Poll` counter, a `PageLifetime`-cancelled fake download, `WhileVisible` on `Connectivity.ConnectivityChanged`), listed as "Page lifetime".
- Docs: "Work that lives with the page" in `regions.md`, a pointer in `page-pattern.md`, the `/spine-page` skill.
- Verified on the Pixel 10 Pro emulator by screenshots at 1 s, 6 s, after 6 s in the background and 5 s later: 1, 7, 9, 14 ticks, i.e. one per second on screen, one immediate tick on return, none meanwhile. iOS checked the same way on the iPhone 17 simulator.

## Decisions

- Polls run on the UI thread as async loops (`PeriodicTimer` awaited on the main context) rather than on a thread pool timer, so `work` can touch bound properties directly and the pause is a plain cancellation.
- Deactivation counts as a pause even for a notification shade or a system dialog; the cost is one extra immediate run on activation, the gain is one rule.
