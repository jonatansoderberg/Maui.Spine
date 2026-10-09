# Guards, biometrics, app lock and privacy shield

**Status:** Proposal, 2026-10-09, with the owner's answers in [Decisions](#decisions-2026-10-09). Not started. Roadmap: [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505). Issues: [#506](https://github.com/jonatansoderberg/Maui.Spine/issues/506) (core guards, §2), [#507](https://github.com/jonatansoderberg/Maui.Spine/issues/507) (`IBiometricAuth` and `[RequiresUnlock]`, §3–§4), [#508](https://github.com/jonatansoderberg/Maui.Spine/issues/508) (app lock and privacy shield, §5). Visual concepts: https://claude.ai/artifact/J5TCSDk8Rk1vZNti9MBM3J. Nothing here has been built or run; Spine facts are read from the source on `origin/master` (1ab4b45), platform facts from documentation and the reference packs.
**Question:** How can Spine run something before it shows a page or runs a command (an unlock prompt, a sign-in sheet) without the core knowing about biometrics or authentication, and how should a biometrics package use that for pages, commands, an app lock and a privacy shield?
**Answer:** The core gets one small generic mechanism: an abstract `GuardAttribute` and an `IGuard<TAttribute>` that a package registers. `NavigationService` asks the guards of the target page type before it resolves the page from DI, and Spine's own command sinks (page actions, menus and context menus, action sheets, `Tap.Command`, `{PageCommand}`) ask the guards of the `[RelayCommand]` method they run. A new package `Plugin.Maui.Spine.Biometrics` defines `[RequiresUnlock]` on top of that, plus `IBiometricAuth` (LocalAuthentication, AndroidX Biometric, Windows Hello), `Unlock.Command` and `RequireAsync` for everything Spine does not invoke, a global `AppLock` and a per-page `[PrivacyShield]`. Guards are UI gates, not encryption: a `Command="{Binding DeleteCommand}"` on a plain button bypasses them, and that is stated in the wiki.


## Decisions (2026-10-09)

The owner answered the open questions in §9. Where an answer differs from the text below, the answer wins.

1. A plain `{Binding XCommand}` bypassing the command guard is documented. `Unlock.Command` and `RequireAsync` cover those cases, and no source generator is planned.
2. The default grace period is 1 minute, with `Fresh` for destructive commands.
3. `WhenUnavailable` defaults to `Allow`.
4. The build fails with a clear message when `NSFaceIDUsageDescription` is missing. The package writes no default text.
5. **Changed from the proposal:** a guarded tab on Android and Windows shows the tab under an **unlock cover** until the guard passes, instead of switching back.
6. The desktop app lock counts minimised time only.

---

## 1. The conclusion in short

| Question | Answer | Evidence |
|---|---|---|
| Where does the navigation hook go? | In `NavigationService`, **before** `_services.GetRequiredService(typeof(TNode))`, so a refused page and its view model are never constructed. One private `PassGuardsAsync(Type, GuardTrigger)` called from each public entry. | `NavigationService.cs:42-61, 83-111, 128-166, 187-200, 337-364` |
| User taps on a guarded tab? | iOS refuses the selection in `ShouldSelectViewController` (already assigned) and switches in code after the guard passes. Android and Windows have no pre-select hook in Spine, so the host switches back in `OnCurrentPageChangedCore`. | `SpineTabbedHostPage.Apple.cs:31-37`, `SpineTabbedHostPage.cs:239-253` |
| Where does the command hook go? | At the five places Spine runs or hands out a command: `PageActionView`, `MenuButton.Pick` (every menu and context menu), `ActionSheet.Pick`, `Tap`'s `Execute`, and the `{PageCommand}` binding. The attribute is found when `PageActionDiscovery.Populate` scans the view model, exactly as `[PageAction]` is. | `PageActionView.cs:505,837,849`, `MenuButton.cs:61-77`, `ActionSheet.cs:67-72`, `Tap.cs:157-163`, `PageBindingExtension.cs:63-71`, `PageActionDiscovery.cs:26-81` |
| Can `[RequiresSignIn]` (#519) reuse it? | Yes. Spine.Authentication derives its attribute from `GuardAttribute` and registers its own `IGuard<RequiresSignInAttribute>`. Neither package references the other. | §2.2 |
| Biometrics on all four platforms? | Yes: `LAContext` (iOS, Mac Catalyst), `BiometricPrompt` from `Xamarin.AndroidX.Biometric` 1.1.0.33 (new dependency, not in the graph today), `UserConsentVerifierInterop` with the window handle (Windows). | §3 |
| Block screenshots? | **Android only** (`FLAG_SECURE`), and Windows can exclude the window from capture. iOS can only cover the app-switcher snapshot and detect recording. | §5 |
| Secrets bound to biometrics? | **Out of scope** (owner, 2026-10-09). No Keychain access control, no Keystore `CryptoObject`. | §7 |

---

## 2. Core guards (#506)

### 2.1 What Spine already has

| Part | Where | What it means here |
|---|---|---|
| Public navigation entries | `NavigationService.cs:42` `NavigateToAsync<T>`, `:83` with a parameter, `:114-122` `ShowAsync`, `:169-175` with a result, `:325` `SetRootAsync`, `:64` `SwitchToTabAsync` | Six entries, each resolves the page from DI near its top. The guard goes in front of that. |
| Sheet close before a region page in `ShowAsync` | `NavigationService.cs:148-154` | The guard must run before this, or a refused unlock has already closed the user's sheet. |
| Tab switching | `SpineTabbedHostPage.cs:169-200` `SwitchToAsync`, `:219-235` `EnsureRealizedAsync` (DI resolve), `:239-253` `OnCurrentPageChangedCore` | Code switches and user taps meet here; user taps arrive after the platform has already switched. |
| iOS tab pre-select | `SpineTabbedHostPage.Apple.cs:31-37` assigns `ShouldSelectViewController` and returns `true` | The one platform where a tap can be refused before anything moves. |
| Leave guards | `ViewModelBase.cs:704` `OnBackRequestedAsync`, `:709` `OnCloseRequestedAsync` | The precedent: async `bool`, cancel by returning false. Enter guards are the same shape, but declared by attribute and owned by a package. |
| Page actions | `PageActionDiscovery.cs:26-53` (once per view model, from `NavigableMeta.cs:20`), `:55-81` scan by attribute, `ToolkitNames.CommandFor` maps `DeleteAsync` to `DeleteCommand` | The scan that finds `[PageAction]` on a `[RelayCommand]` method also finds a guard attribute on it. |
| Command sinks | `PageActionView.cs:505,837,849` (`Button.Command = action.Command`); `MenuButton.Pick` `:61-77`, used by `MenuExtensions.Apple.cs:131`, `MenuExtensions.Android.cs:92`, `MenuExtensions.Windows.cs:127`; `ActionSheet.Pick` `:67-72`; `Tap` `:157-163`; `PageCommandExtension` `:63-71` | Every place Spine runs or binds a command. A plain `{Binding XCommand}` is not among them. |
| Ways in from outside | Shortcuts: `MauiAppBuilderExtensions.cs:184-188` hands the id to the app's `IShortcutHandler`, which calls `INavigationService`. Search results: `SearchIndex.cs:135-157` awaits `WhenRootSet` (`NavigationService.cs:316-322`) and calls `ShowAsync` | Both end in a guarded entry. Spine has no deep-link router of its own; app links reach it through the app's own `INavigationService` calls. |
| Window activation | `SpineApplication.cs:85-108` hooks `Window.Deactivated`/`Activated` | Where the app lock and the shield attach (§5). |
| Module registration | `<SpineModule>` in `build/*.props`, run by `UseSpine()` (`SpineModules.cs`, #355); package options through `UseSpineXxx(o => ...)` called again by the app (`SpineBackgroundTasksExtensions.cs:18-36`) | `UseSpineBiometrics(o => o.AppLock(...))` follows the same pattern. |

### 2.2 Design

```csharp
// Plugin.Maui.Spine.Core
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited = true)]
public abstract class GuardAttribute : Attribute
{
    /// <summary>Lower runs first. Sign-in is 100, unlock 200, so a user is signed in before being asked to unlock.</summary>
    public int Order { get; init; }
}

public interface IGuard<in TAttribute> where TAttribute : GuardAttribute
{
    /// <summary>True lets the navigation or command go on. Return a completed ValueTask when nothing needs asking.</summary>
    ValueTask<bool> CheckAsync(TAttribute attribute, GuardContext context, CancellationToken cancellationToken);
}

public sealed record GuardContext(GuardTrigger Trigger, Type? Page, string? Command, bool CanCancel);

public enum GuardTrigger { Push, Sheet, Tab, Show, Root, Command }

public static class SpineGuardServiceCollectionExtensions
{
    public static IServiceCollection AddSpineGuard<TAttribute, TGuard>(this IServiceCollection services)
        where TAttribute : GuardAttribute where TGuard : class, IGuard<TAttribute>;
}
```

An internal `GuardRunner` reads the guard attributes of a type or member once (cached per `MemberInfo`, like `PageActionDiscovery._templates`), orders them by `Order`, resolves `IGuard<T>` for each and runs them one after the other. The first `false` stops the rest. An attribute without a registered guard throws at the first use with the attribute's name, so a missing package reference is not silently an open door. The core ships no guard apart from test doubles.

**Navigation hook points.** One call, `PassGuardsAsync(pageType, trigger)`, at:

| Entry | Place | On refusal |
|---|---|---|
| `NavigateToAsync<T>` / with a parameter | Before the tab branch and before `GetRequiredService` (`:45`, `:87`) | Returns; nothing constructed, parameter not delivered |
| `ShowAsync` | At the top of `ShowCoreAsync`, before the sheet close (`:130`); the inner `navigate()` then skips the guard | Returns; the open sheet stays |
| `NavigateToWithResultAsync` | Before `GetRequiredService` (`:195`) | `NavigationResult<T>.Canceled()`, the existing value |
| `SwitchToTabAsync` | Before `SwitchToTabCoreAsync` | Returns |
| User tab tap, iOS | `ShouldSelectViewController`: if `CheckAsync` completes synchronously with `true`, return `true`; otherwise return `false`, run the guards, then `SwitchToAsync` | The tab never changes |
| User tab tap, Android/Windows | `OnCurrentPageChangedCore`, before `ActivateSlotAsync`: set `CurrentPage` back with `_switchingInCode`, run the guards, switch in code on success | A short flash of the tab bar selection |
| `SetRootAsync` | Before `SetRootCoreAsync` (`:329`) | Keeps the current root; see cold start |

Back navigation and `ReturnAsync` run no guards: a page below in the stack has already passed, and relocking is the app lock's job (§5).

### 2.3 Command guard

A guard attribute on a `[RelayCommand]` method or an `ICommand` property is found by the same member scan as `[PageAction]` (`PageActionDiscovery.Scan`). For every view model instance, `Populate` records the command instance and its attributes in a `ConditionalWeakTable<ICommand, GuardAttribute[]>`. An internal `CommandGuards.Wrap(ICommand)` returns the command itself when it has no guard, otherwise a cached wrapper that forwards `CanExecute`/`CanExecuteChanged` and runs the guards before `Execute` (and implements `IAsyncRelayCommand` by forwarding when the inner command does, so `PageAction.AsyncCommand` keeps working).

The sinks call it: `PageActionView` binds `CommandGuards.Wrap(action.Command)` instead of `action.Command`, which also covers a `PageAction` built by hand in the view model; `MenuButton.Pick`, `ActionSheet.Pick` and `Tap`'s `Execute` call `CommandGuards.ExecuteAsync(command, parameter)`; `PageCommandExtension` adds a converter that wraps the bound command. Lookup is by instance, so a command shared by a menu row and a header button is guarded in both.

**What it does not cover.** A command from a view model that is not a Spine page's (a row view model) is never scanned, and a plain `Command="{Binding DeleteCommand}"` hands the raw command to MAUI's `Button`. Reading the private delegate inside CommunityToolkit's `RelayCommand` to find the method was considered and rejected: it is not a public contract. For those cases there is `Unlock.Command` and `RequireAsync` (§4), and the wiki says plainly that a guard is a UI gate: the code that must never run without an unlock calls `RequireAsync` itself.

### 2.4 Ordering, cancellation and re-entrancy

- **Order:** by `Order`, then by attribute type name for a stable result. Page guards and command guards are separate lists; a command on a guarded page does not run the page's guard again.
- **Cancellation:** the token is cancelled when the host is swapped (`SwapHost`, `NavigationService.cs:366`), for example on sign-out, so a pending prompt does not lead into a page of the old session. A guard that throws counts as a refusal and is logged as an error.
- **What the user sees on refusal:** nothing changes. The guard owns any message (the biometrics guard shows an alert for lock-out, never for a cancel). The core logs at Debug level which guard refused which target.
- **Two navigations while a prompt shows:** the core does not serialise navigation today and does not start to. Two rules keep it sane: (1) a navigation to a page type that is already waiting on its guards is dropped, so a double tap on a row gives one prompt and one push; (2) guards coalesce their own UI: `IUnlock.RequireAsync` returns the in-flight task to a second caller, so a page push and a shortcut arriving together share one Face ID prompt and both proceed in request order once it passes, or neither does. A command that is waiting on its guard ignores further executions.

### 2.5 Cold start through a shortcut or a search result

`CreateWindow` starts `SetRootAsync<TRoot>` (`SpineApplication.cs:76`). The shortcut handler or `SearchIndex.OpenAsync` then calls a guarded entry. A biometric prompt cannot be shown before the app is active: iOS refuses evaluation from a non-interactive app, and `BiometricPrompt` needs a resumed `FragmentActivity`. `GuardRunner` therefore awaits `WhenRootSet` and the window's first `Activated` before calling any guard, and the guard runs over the root page. If the app lock is on and locks at launch, its unlock screen is already up; the `[RequiresUnlock]` guard sees the lock's in-flight unlock and awaits it instead of asking twice. On a refusal the user is left on the root page.

A guard on the root page itself or on the first tab (`Trigger = Root`, or the tab realised at launch) gets `CanCancel = false`: there is nothing to stay on. The biometrics guard then shows its unlock screen with a retry button until it passes, the same screen as the app lock. Spine.Authentication's guard shows its sign-in sheet the same way.

### 2.6 Alternatives

| | Attribute + `IGuard<T>` (proposal) | One `INavigationInterceptor` with the full request | Each package patches the core |
|---|---|---|---|
| Declarative per page and command | Yes | Only if the interceptor reads attributes itself | Yes |
| Two packages side by side | Ordered by `Order` | Interceptor order by registration | Conflicts |
| Core surface | One attribute base, one interface, one registration | One interface, but every package re-implements the attribute reading | None, but core changes per package |

An interceptor could also redirect (show page B instead of A). Nothing on the roadmap needs that, so it stays out.

---

## 3. `IBiometricAuth` and platforms (#507)

### 3.1 iOS and Mac Catalyst

`LAContext.CanEvaluatePolicy` then `EvaluatePolicyAsync` with `LAPolicy.DeviceOwnerAuthentication` (biometrics with the passcode as fallback) or `DeviceOwnerAuthenticationWithBiometrics`. `BiometryType` says which: `TouchId`, `FaceId` or `OpticId`; the Microsoft.iOS 26.2 reference pack binds all three (checked with `strings` on the pack). Optic ID exists only on Apple Vision Pro, where iPad apps can run. `LocalizedCancelTitle` and `LocalizedFallbackTitle` are set from Spine's strings. Every evaluation gets a fresh `LAContext`, so a success is not reused behind Spine's back.

**`NSFaceIDUsageDescription`.** Required in Info.plist for Face ID. Without it the app is terminated by the privacy check when it evaluates Face ID on a device (Apple forum reports, see sources; not reproduced here). Spine has two existing patterns: the Scanner checks `NSCameraUsageDescription` at run time and reports instead of crashing (`BarcodeScannerViewHandler.Apple.cs:291-293`), and `Plugin.Maui.Spine.Common.targets` writes a partial plist for keys that several packages share (`_SpineWriteBackgroundModes`, `:90-158`). Proposal: both. A `_SpineWriteFaceIdUsage` target writes `NSFaceIDUsageDescription` from `$(SpineFaceIdUsageDescription)` into a partial plist **only when the app's Info.plist lacks it** (a partial plist would otherwise override the app's own text), and `IBiometricAuth` refuses Face ID with `BiometricResult.NotAvailable` and a logged reason when the key is missing at run time. A plain string key merges cleanly; only arrays are replaced (`Common.targets:77-80`).

### 3.2 Android

`BiometricPrompt` from `androidx.biometric`. It needs a `FragmentActivity`. MAUI's `MainActivity` derives from `MauiAppCompatActivity`, an `AppCompatActivity`, which is a `FragmentActivity`; Spine already casts `Platform.CurrentActivity` to `AppCompatActivity` (`SpineApplication.Android.cs:111`). Allowed authenticators default to `BIOMETRIC_WEAK | DEVICE_CREDENTIAL`: weak is enough without a `CryptoObject`, it includes the Class 2 face unlock on many phones, and Android's documentation lists only `DEVICE_CREDENTIAL` alone and `BIOMETRIC_STRONG | DEVICE_CREDENTIAL` as unsupported on API 29 and lower. With `DEVICE_CREDENTIAL` a negative button text is not allowed. `BiometricManager.CanAuthenticate` gives availability; which modality the user enrolled is not exposed, so `Kind` is `Biometric` on Android (with `PackageManager` features as a hint only).

**Dependency.** `Xamarin.AndroidX.Biometric` is not in `Directory.Packages.props` and not in the local NuGet cache, so nothing in Spine or MAUI pulls it today. 1.1.0.33 (2026-06-27) targets `net9.0-android35.0` and `net10.0-android36.0` and depends on Activity 1.13.0.1, AppCompat 1.7.1.4, Core 1.19.0.1, Fragment ≥ 1.8.9.3 and Lifecycle LiveData.Core/ViewModel 2.11.0.1. Lifecycle 2.11 is the same line Spine already lifts for Push (`Directory.Packages.props:44-51`) because MAUI 10.0.50 pins 2.9.2.1; Biometrics needs the same pins. Activity and Core above MAUI's own are the binding-drift risk from #451 and must be checked with a build of an app that has Push and Biometrics together.

### 3.3 Windows

`UserConsentVerifier.CheckAvailabilityAsync()` for availability. For a desktop (WinUI 3) app Microsoft's documentation says not to call `RequestVerificationAsync` but `UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd, message)`; Spine already gets the HWND (`SpineApplication.Windows.cs:291`). Windows Hello includes the PIN, so the device credential is always part of it and `Kind` is `WindowsHello`. No package: the projection is in the Windows SDK that `net10.0-windows` already references.

### 3.4 Platform table

| | Kinds reported | Passcode fallback | Prompt UI | Needs |
|---|---|---|---|---|
| iOS | FaceId, TouchId, OpticId | Yes (`DeviceOwnerAuthentication`) | System | `NSFaceIDUsageDescription` |
| Mac Catalyst | TouchId | Mac password | System | Nothing extra |
| Android | Biometric | PIN/pattern/password | System (BiometricPrompt) | `Xamarin.AndroidX.Biometric`; `USE_BIOMETRIC` from the AAR manifest (to confirm in the merged manifest) |
| Windows | WindowsHello | PIN inside Hello | System | Window handle |

---

## 4. `[RequiresUnlock]` on pages and commands (#507)

```csharp
public sealed class RequiresUnlockAttribute(string reason) : GuardAttribute
{
    public string Reason { get; } = reason;          // a Strings key or text, resolved like other Spine strings
    public bool Fresh { get; init; }                 // ask again even inside the grace period
    public bool Relock { get; init; }                // cover the page again after the app lock's LockAfter (§5)
}

[NavigableRegion(Title = "Card details"), RequiresUnlock("Show your card details")]
public partial class CardPage : SpinePage<CardViewModel>;

public partial class AccountViewModel : ViewModelBase
{
    [RelayCommand, PageAction("Delete", Svg = "delete.svg"), RequiresUnlock("Delete your account", Fresh = true)]
    private Task DeleteAsync() => _account.DeleteAsync();
}
```

`Order` is 200 by default. The guard is `UnlockGuard : IGuard<RequiresUnlockAttribute>`, which calls `IUnlock.RequireAsync`.

**Plain buttons and any tappable view:** `Unlock.Command` and `Unlock.Reason` attached properties. On a `Button` it sets `Button.Command` to a wrapper; on any other view it sets `Tap.Command`, so the press feedback and haptics of `Tap` stay.

```xml
<Button Text="Show PIN" spine:Unlock.Command="{Binding ShowPinCommand}" spine:Unlock.Reason="Show the PIN" />
<Border spine:Unlock.Command="{Binding OpenVaultCommand}" spine:Unlock.Reason="Open the vault"> ... </Border>
```

**Imperative:** `IUnlock`.

```csharp
public interface IUnlock
{
    bool IsUnlocked { get; }                            // inside the grace period and not locked by the app lock
    Task<bool> RequireAsync(string reason, bool fresh = false, CancellationToken cancellationToken = default);
    void Lock();                                        // e.g. on sign-out
    event EventHandler? LockChanged;
}
```

**How long an unlock lasts.** One unlock state per app. `o.UnlockValidFor` (default 1 minute) is the grace period; `Fresh = true` ignores it, which suits a destructive command. The state is cleared when the app goes to the background, so returning from the app switcher always asks again for a guarded page. Concurrent `RequireAsync` calls share one prompt (§2.4).

**Unavailable or not enrolled.** `o.WhenUnavailable`: `Allow` (default: a device with no passcode has no lock to ask for, and the guard is a UI gate) or `Deny` with an alert. Lock-out (too many tries) is always a refusal with a localised alert.

Sample: a "Private" page in Spine Showcase with a guarded page, a guarded page action, `Unlock.Command` on a button, and the app lock switch. Wiki: `docs/wiki/biometrics.md`.

---

## 5. App lock and privacy shield (#508)

### 5.1 What each platform allows

| | Hide the app-switcher snapshot | Block screenshots and recording | Detect |
|---|---|---|---|
| iOS | Yes: cover the window in `sceneWillResignActive`, before the system takes the snapshot | **No.** Not possible with public API | `UIScreen.IsCaptured` / scene capture state (recording, mirroring); `UserDidTakeScreenshotNotification` after the fact |
| Mac Catalyst | No snapshot exists | Not in v1 (`NSWindow.sharingType` is AppKit, out of reach without a bridge) | No |
| Android | `FLAG_SECURE` also blanks the recents thumbnail; `Activity.SetRecentsScreenshotEnabled(false)` (API 33) without blocking screenshots | **Yes**, `FLAG_SECURE` on the window; also on a sheet's window, since Spine's sheets are a `BottomSheetDialog` (`BottomSheetPageExtensions.Android.cs:115-120, 498`) | `RegisterScreenCaptureCallback` (API 34) not in v1 |
| Windows | No snapshot to hide | `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`, Windows 10 2004 and later | No |

The "secure text field layer" trick some iOS apps use to hide content from screenshots relies on private behaviour of `UITextField` and is not proposed.

### 5.2 `[PrivacyShield]` per page

```csharp
[NavigableRegion(Title = "Statement"), PrivacyShield, RequiresUnlock("Show the statement", Relock = true)]
public partial class StatementPage : SpinePage<StatementViewModel>;
```

While the page is the current page of the root region or the open sheet (`SpineApplication.ShownViewModels`), the package sets `FLAG_SECURE` on Android and the capture exclusion on Windows, and covers the window when the app leaves the foreground on iOS. Optional `CoverWhileCaptured` covers it while iOS reports the screen as recorded or mirrored.

The cover is **native**, not a MAUI view: a `UIVisualEffectView` added to the `UIWindow` synchronously in the resign callback, because the snapshot follows right after it and a MAUI layout pass is not guaranteed in between (reasoned, not measured). It looks like `Material.Kind = Blur` (`Material.cs:16`) with the app icon. Where `Material` falls back to tint alone (Android before 12), the shield uses an opaque fill, since a translucent tint would show the content.

`Relock = true` on a `[RequiresUnlock]` page: after `LockAfter` in the background the page stays where it is but is covered by the unlock screen until the user unlocks; it is not popped, so its state is kept.

### 5.3 Global app lock

```csharp
builder.UseSpineBiometrics(o => o.AppLock(a =>
{
    a.LockAfter = TimeSpan.FromMinutes(5);   // TimeSpan.Zero locks on every return
    a.LockOnLaunch = true;
    a.Reason = "Unlock Orientera";
    a.IsEnabled = () => settings.RequireUnlock; // the app's own user setting
}));
```

The timer starts at MAUI's `Window.Stopped` (background on iOS and Android, minimised on desktop; desktop behaviour to be verified), not at `Deactivated`. Two reasons: the Face ID sheet itself makes an iOS app inactive, so counting from `Deactivated` would lock on every prompt, and pulling down Control Center should not lock. The global shield still goes up at `Deactivated` while the lock is enabled, and is skipped while Spine's own prompt is showing. On return after `LockAfter`, an unlock screen covers the host page (the root page is built underneath, so a cold start is not slower) and asks at once; Cancel leaves the screen with an Unlock button. A successful app unlock satisfies `[RequiresUnlock]` for the grace period, so the user is asked once.

Orientera (personal training data), Almanacka (private notes) and Puckkoll (none) show the range: off by default, enabled by an app setting.

---

## 6. API surface and packages

**Core (`Plugin.Maui.Spine`)**: `GuardAttribute`, `IGuard<T>`, `GuardContext`, `GuardTrigger`, `AddSpineGuard<TAttribute, TGuard>()`, and the internal `GuardRunner`, `CommandGuards`. No new dependency.

**`Plugin.Maui.Spine.Biometrics`** (new): `IBiometricAuth`, `IUnlock`, `RequiresUnlockAttribute`, `PrivacyShieldAttribute`, `Unlock` attached properties, `UseSpineBiometrics(o => ...)` registered as a `<SpineModule>`. Depends on `Plugin.Maui.Spine`. Pulls in `Xamarin.AndroidX.Biometric` and its AndroidX graph on Android only; nothing on Apple or Windows.

```csharp
public interface IBiometricAuth
{
    Task<BiometricAvailability> GetAvailabilityAsync();
    Task<BiometricResult> AuthenticateAsync(string reason, BiometricOptions? options = null, CancellationToken cancellationToken = default);
}

public sealed record BiometricAvailability(BiometricKind Kind, bool Enrolled, bool HasDeviceCredential);
public enum BiometricKind { None, FaceId, TouchId, OpticId, Biometric, WindowsHello }
public enum BiometricResult { Success, Canceled, Failed, LockedOut, NotEnrolled, NotAvailable }

public sealed class BiometricOptions
{
    public bool AllowDeviceCredential { get; init; } = true;
    public string? Title { get; init; }          // Android's prompt title; iOS and Windows show the reason only
}

public sealed class SpineBiometricsOptions
{
    public TimeSpan UnlockValidFor { get; set; } = TimeSpan.FromMinutes(1);
    public UnavailablePolicy WhenUnavailable { get; set; } = UnavailablePolicy.Allow;
    public SpineBiometricsOptions AppLock(Action<AppLockOptions> configure);
}
```

**`Plugin.Maui.Spine.Authentication`** (#519, separate proposal) uses only the core part.

---

## 7. Out of scope

- **Secrets bound to biometrics**: Keychain items with `SecAccessControl` (`.biometryCurrentSet`), Android Keystore keys with `setUserAuthenticationRequired` and a `CryptoObject`. Dropped by the owner on 2026-10-09. Without it, an unlock protects the UI, not data at rest.
- Redirecting guards (go to B instead of A), screenshot detection on Android 14, Mac capture exclusion, the iOS 18 system-wide "Require Face ID" for any app (the user's own choice; the wiki can point to it).

---

## 8. Implementation steps

1. **Core guards** (#506): `GuardAttribute`, `IGuard<T>`, `GuardRunner` with ordering, coalescing per target and the interactive wait; the hook points in §2.2; `CommandGuards` and the five sinks in §2.3. Unit tests with a test guard: refusal constructs no page, `ShowAsync` keeps the sheet, result navigation returns `Canceled`, double navigation gives one push, a missing guard registration throws.
2. **Tab taps**: iOS `ShouldSelectViewController`, Android/Windows switch-back. Try on the simulator and the emulator.
3. **`IBiometricAuth`** on the four platforms, the Info.plist target and the run-time key check, the AndroidX pins. Build an app with Push and Biometrics together for the Android graph.
4. **`IUnlock`, `[RequiresUnlock]`, `Unlock.Command`**, strings in `SpineStrings`, alerts.
5. **App lock and `[PrivacyShield]`**: native covers, `FLAG_SECURE` on the activity and sheet windows, capture exclusion on Windows.
6. **Sample, wiki, skill** (`spine-page` gets a guards section), and a device pass: Face ID on the iPhone, a cold start through a shortcut to a guarded page with the lock on, the app switcher, Android screenshot blocked, Windows Hello with PIN.

---

## 9. Open questions for the owner

1. Is a guard on a plain `{Binding XCommand}` being bypassed acceptable as documented, or should Spine offer a source generator later?
2. Default grace period: 1 minute with `Fresh` for destructive commands, or every guard asks every time?
3. `WhenUnavailable` default: `Allow` (no device lock means nothing to ask) or `Deny`?
4. Should the package write a default English `NSFaceIDUsageDescription`, or only check and fail the build with a message, as the entitlements target does?
5. Tab taps on Android/Windows: accept the brief switch-back, or show the tab with an unlock cover instead?
6. Desktop: should the app lock count minimised time only, or also idle time while the window is open?

---

## 10. Sources

Read in the repo: `NavigationService.cs`, `NavigationRegistry.cs`, `NavigableMeta.cs`, `PageActionDiscovery.cs`, `ToolkitNames.cs`, `PageBindingExtension.cs`, `Tap.cs`, `MenuButton.cs`, `ActionSheet.cs`, `MenuExtensions.*.cs`, `PageActionView.cs`, `PageAction.cs`, `SpineTabbedHostPage.cs`, `SpineTabbedHostPage.Apple.cs`, `SpineTabbedHostPage.Android.cs`, `SpineApplication.cs`, `SpineApplication.Android.cs`, `SpineApplication.Windows.cs`, `ViewModelBase.cs`, `SearchIndex.cs`, `MauiAppBuilderExtensions.cs`, `SpineModules.cs`, `Material.cs`, `BottomSheetPageExtensions.Android.cs`, `BarcodeScannerViewHandler.Apple.cs`, `Plugin.Maui.Spine.Common.targets`, `SpineBackgroundTasksExtensions.cs`, `Directory.Packages.props`, `docs/wiki/packages.md`.

- Apple, `LAContext`: https://developer.apple.com/documentation/localauthentication/lacontext
- Apple, `LABiometryType`: https://developer.apple.com/documentation/localauthentication/labiometrytype
- Apple, `NSFaceIDUsageDescription`: https://developer.apple.com/documentation/bundleresources/information-property-list/nsfaceidusagedescription
- Apple Developer Forums, Face ID without the usage key: https://developer.apple.com/forums/thread/86779 and https://developer.apple.com/forums/thread/732651
- Apple, *Preparing your UI to run in the background* (snapshot): https://developer.apple.com/documentation/uikit/preparing-your-ui-to-run-in-the-background
- Apple, `UIScreen.isCaptured`: https://developer.apple.com/documentation/uikit/uiscreen/iscaptured
- Android, *Show a biometric authentication dialog*: https://developer.android.com/identity/sign-in/biometric-auth
- Android, `WindowManager.LayoutParams.FLAG_SECURE`: https://developer.android.com/reference/android/view/WindowManager.LayoutParams#FLAG_SECURE
- Android, `Activity.setRecentsScreenshotEnabled`: https://developer.android.com/reference/android/app/Activity#setRecentsScreenshotEnabled(boolean)
- NuGet, `Xamarin.AndroidX.Biometric` 1.1.0.33: https://www.nuget.org/packages/Xamarin.AndroidX.Biometric
- Microsoft, `UserConsentVerifier` (desktop apps and `UserConsentVerifierInterop`): https://learn.microsoft.com/en-us/uwp/api/windows.security.credentials.ui.userconsentverifier
- Microsoft, `SetWindowDisplayAffinity`: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- Spine.Authentication proposal (#519): https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/proposals/spine-authentication.md
