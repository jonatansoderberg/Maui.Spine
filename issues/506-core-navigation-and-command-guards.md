# Issue #506 — Core: navigation and command guards

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/506
**Branch:** issue/506-core-navigation-and-command-guards
**Status:** In Progress

Part of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), step 1.1 in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-guards-biometrics.md` §2, together with the owner's decisions of 2026-10-09 at the top of that file.

## Plan

The core gets one generic mechanism. Packages (Biometrics #507, Authentication #519) define their own attribute and guard on top of it. The core ships no guard of its own; the only guards are test doubles.

### 1. Public surface (`src/Plugin.Maui.Spine/Core/Guards/`)

- `GuardAttribute`: abstract, with `Order` (lower runs first). It can be put on a class, a method or a property, and is inherited.
- `IGuard<in TAttribute>`: `ValueTask<bool> CheckAsync(TAttribute, GuardContext, CancellationToken)`.
  - It also has a default interface method `View? CreateCover(TAttribute, GuardContext, Func<Task> retry) => null`. Its use is described under tab taps in §3.
- `GuardContext(GuardTrigger Trigger, Type? Page, string? Command, bool CanCancel)`.
- `GuardTrigger`: `Push`, `Sheet`, `Tab`, `Show`, `Root`, `Command`.
- `AddSpineGuard<TAttribute, TGuard>()`: an `IServiceCollection` extension that registers the guard as a singleton.

### 2. `GuardRunner` (internal, no MAUI types, so it can be unit tested)

- **Attributes:** reads the guard attributes of a `MemberInfo` once and caches them per member. Orders them by `Order`, then by type name.
- **Running:** resolves `IGuard<T>` for each attribute through a cached generic adapter and runs them one after the other. The first `false` stops the rest.
- **Failures:**
  - A guard that throws counts as a refusal and is logged as an error.
  - An attribute with no registered guard throws `InvalidOperationException` naming the attribute and the package that probably has to be referenced, so a missing reference is not silently an open door.
- **Double navigation:** a navigation to a page type that is already waiting on its guards is dropped (`false`), so a double tap gives one prompt.
- **Readiness gate:** an `await` before any guard runs.
  - The window must have been activated at least once.
  - For every trigger except `Root`, `WhenRootSet` must also have completed.
  - This way a shortcut or search result on a cold start never prompts before the app can show a prompt.
- **Cancellation:** the token is cancelled when `SwapHost` swaps the host, so a pending prompt does not lead into the old session.

### 3. Navigation hook points (`NavigationService`)

`PassGuardsAsync(Type page, GuardTrigger trigger)` is called **before** `GetRequiredService`, so a refused page and its view model are never constructed.

| Entry | On refusal |
|---|---|
| `NavigateToAsync<T>` / with a parameter | Nothing happens; the parameter is not delivered |
| `ShowAsync` | Nothing happens, and an open sheet stays open. The guard runs in `ShowCoreAsync` before the sheet is closed, and the inner navigate does not ask again. |
| `NavigateToWithResultAsync` | `NavigationResult<T>.Canceled()` |
| `SwitchToTabAsync` and navigating to a tab page | Nothing happens |
| `SetRootAsync` | Runs with `CanCancel = false` and keeps the current root. On a cold start the host shows an empty root region while the guard shows its own UI. |

Back navigation and `ReturnAsync` run no guards.

**Tab taps by the user**
- **iOS:** `ShouldSelectViewController` returns `false` for a guarded tab that has not passed, runs the guards, and then calls `SwitchToAsync` in code when they pass.
- **Android and Windows** (owner decision 5: an unlock cover, not a switch-back):
  - `OnCurrentPageChangedCore` keeps the tab selected but does not realise its root page yet.
  - If the guard provides a cover (`CreateCover`), that view is set as the tab's content while the guard runs and stays after a refusal, with the guard's own retry button.
  - When the guard passes, the cover is replaced by the real root page.
  - A guard with no cover (for example a future sign-in guard that opens a sheet) falls back to switching back to the previous tab.

### 4. Command guards

- **Discovery:** `PageActionDiscovery.Scan` also collects guard attributes on `[RelayCommand]` methods and `ICommand` properties, with or without `[PageAction]`. `Populate` records each command instance with its attributes in a `ConditionalWeakTable<ICommand, GuardAttribute[]>` inside `CommandGuards`.
- **The wrapper:** `CommandGuards.Wrap(ICommand)` returns the command unchanged when it has no guard. Otherwise it returns a cached wrapper that:
  - forwards `CanExecute` and `CanExecuteChanged`
  - runs the guards before `Execute`
  - implements `IAsyncRelayCommand` by forwarding when the inner command does
  - ignores further executions while the guard is pending
- **The sinks that use it:**
  - `PageActionView` (three places: `Command = CommandGuards.Wrap(action.Command)`)
  - `MenuButton.Pick`, which covers every menu and context menu
  - `ActionSheet.Pick`
  - `TapState.Execute`
  - `{PageCommand}`, through a binding converter that wraps the command
- **Not covered:** a plain `{Binding XCommand}` bypasses the guard (owner decision 1). That is documented in the wiki.

### 5. Tests (`tests/Plugin.Maui.Spine.Core.Tests`)

`GuardRunner`, `CommandGuards` and the attribute discovery have no MAUI types and are compiled into the test project, the same way `ActionSheet.cs` is. The tests cover:
- ordering
- the first refusal stopping the rest
- a guard that throws counting as a refusal
- a missing registration throwing with a helpful message
- double navigation being dropped
- the command wrapper forwarding `CanExecute`, `IAsyncRelayCommand` and refusal

`NavigationService` needs MAUI, so it is verified in the sample instead.

### 6. Sample, docs and plan

- **Sample:** a small "Guards" page in Spine Showcase, using a test guard defined in the sample (a confirm dialog). It shows a guarded page, a guarded page action, a guarded menu row and a guarded tab. The biometrics package replaces the dialog in #507.
- **Docs:** a new `docs/wiki/guards.md`, a guards section in `.claude/skills/spine-page/SKILL.md`, and the status of step 1.1 in `docs/plans/ai-and-security.md`.

### Files

- New: `Core/Guards/GuardAttribute.cs`, `IGuard.cs`, `GuardContext.cs`, `GuardRunner.cs`, `CommandGuards.cs`, `SpineGuardServiceCollectionExtensions.cs`
- Changed: `Services/NavigationService.cs`, `Services/PageActionDiscovery.cs`, `Presentation/SpineTabbedHostPage.cs` (+ `.Apple.cs`), `Presentation/PageActionView.cs`, `Extensions/MenuButton.cs`, `Core/Menu/ActionSheet.cs`, `Extensions/Tap.cs`, `Extensions/PageBindingExtension.cs`, `Extensions/MauiAppBuilderExtensions.cs` (registration), `Core/SpineApplication.cs` (first activation)

## Open Questions

None. The owner answered the proposal's questions on 2026-10-09. The tab cover is implemented through `IGuard.CreateCover`, so the core stays free of biometrics.

## Changes

- **Public API (`Core/Guards/`):** `GuardAttribute`, `IGuard<TAttribute>`, `GuardContext`, `GuardTrigger`, `IGuardCover` and `AddSpineGuard<TAttribute, TGuard>()`.
- **`GuardRunner` (`Services/`):**
  - Reads and caches guard attributes per member, ordered by `Order`.
  - Resolves guards through `GuardRegistration`, and falls back to the guard of a base attribute.
  - Throws on an unregistered attribute, and counts a guard that throws as a refusal.
  - Drops a second navigation to a page that is still asking.
  - Waits for the window's first activation and for the root page (except for `Root`).
  - `CancelPending()` is called from `SwapHost`.
- **`SpineReadiness`:** first activation and root set. `NavigationService.WhenRootSet` reads from it, and `SpineApplication.HookResumed` marks activation.
- **`NavigationService`:**
  - Guards run before the page is resolved in `NavigateToAsync` (with and without a parameter), `SwitchToTabAsync`, `ShowAsync` (before the sheet is closed), `NavigateToWithResultAsync` (refusal gives `Canceled`) and `SetRootAsync` (`CanCancel = false`).
  - The internal navigations skip the guard so it is not asked twice.
- **`SpineTabbedHostPage`:**
  - iOS refuses a guarded tab in `ShouldSelectViewController` (`AllowTabTap`) and switches in code after the guard passes.
  - Android and Windows put the guard's cover (`IGuardCover`) in place of the tab's page while the guard asks, or switch back when there is no cover.
- **`CommandGuards`:**
  - Finds guarded commands when `PageActionDiscovery.Populate` runs.
  - `Execute` runs them after the guard, ignoring repeat taps while it asks.
  - `Wrap` gives a control a command that looks the guard up when it runs.
  - Used by `PageActionView` (the three header-bar buttons), `MenuButton.Pick`, `ActionSheet.Pick`, `TapState.Execute` and `{PageCommand}` (a converter).
- **Tests:** 18 new tests in `Plugin.Maui.Spine.Core.Tests/GuardTests.cs`. The guard files are compiled into the test project. 99/99 pass.
- **Sample:** a Guards page in Spine Showcase with a sample `ConfirmGuard` (a confirmation alert):
  - a guarded page (`VaultPage`)
  - a guarded command from a `Tap.Command` row, an action sheet row and a menu row
- **Docs:** `docs/wiki/guards.md`, links from `getting-started.md` and the core README, a guards section in `.claude/skills/spine-page/SKILL.md`, and a note on `{PageCommand}`.

## Verified

- The core builds for iOS, Android, Mac Catalyst and Windows (Windows compiled on the Mac, not run).
- On the iPhone 17 simulator:
  - The guarded page asks first, and Cancel leaves the user on the Guards page with nothing created.
  - The `Tap.Command` row asks before deleting.
- **Not verified:**
  - Continue on the page and the command, plus the action-sheet and menu rows. The disk filled up mid-test and the app was stopped.
  - Guarded tabs on any platform: Spine Showcase has no tabs.
  - Android and Windows at run time.

## Decisions

- The cover is a separate interface, `IGuardCover`, not a default method on `IGuard<T>`. That keeps `IGuard<T>` free of MAUI types, so the runner and its tests compile without MAUI.
- `CommandGuards.Wrap` looks the guard up when the command runs, not when it is bound. A `{PageCommand}` binding inside the page is evaluated in the page's constructor, before `Populate` has scanned the view model.
- The wrapper is a plain `ICommand`, not an `IAsyncRelayCommand` as the proposal had it. Controls only need `ICommand`, and the inner command's own `IsRunning` still works because the inner command is the one that runs.
- The header bar shows one secondary action. On the sample pages, that is the theme menu, so the sample shows the menu and action-sheet paths instead of a header-bar trash can. The header-bar path goes through the same `CommandGuards.Wrap`.
- The tab cover on Android and Windows (owner decision 5) is generic: a guard may supply a cover view through a default interface method on `IGuard<T>`. Without one, the core falls back to switching back, so a guard such as sign-in, which opens a sheet, still works.
