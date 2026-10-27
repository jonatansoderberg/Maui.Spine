# Biometrics

`Plugin.Maui.Spine.Biometrics` asks the user to unlock before a page opens or a command runs. Each platform uses its own unlock:

- **iPhone and iPad:** Face ID or Touch ID.
- **Mac:** Touch ID.
- **Apple Vision Pro:** Optic ID.
- **Android:** the fingerprint or face prompt.
- **Windows:** Windows Hello.

Each falls back to the device's passcode, PIN or password.

The package also has an [app lock](#the-app-lock) for the whole app and a [privacy shield](#a-page-out-of-screenshots) for single pages.

An unlock protects the user interface, not data at rest. Secrets bound to biometrics (Keychain access control, an Android Keystore `CryptoObject`) are not part of this package.

```bash
dotnet add package Plugin.Maui.Spine.Biometrics
```

`UseSpine` registers the package. Call `UseSpineBiometrics` only to change the options:

```csharp
builder.UseSpineBiometrics(o =>
{
    o.UnlockValidFor = TimeSpan.FromMinutes(5);      // default: one minute
    o.WhenUnavailable = UnavailablePolicy.Deny;      // default: Allow
    o.AppLock();                                     // default: off
});
```

---

## A page behind an unlock

```csharp
[NavigableRegion(Title = "PIN"), RequiresUnlock("Show the PIN")]
public partial class PinPage { … }
```

`[RequiresUnlock]` is a [guard](guards.md). The page and its view model are created only after the unlock, on every way to the page:
- a push or a sheet
- `ShowAsync`
- a result navigation
- a tab
- `SetRootAsync`

On Android and Windows a guarded tab shows a "Locked" cover with an Unlock button until the user unlocks. On iOS the tab does not switch until the user unlocks.

The reason is shown in the platform's prompt. It can be plain text or the key of a [Spine string](strings.md).

## A command behind an unlock

```csharp
[PageAction(Svg = "delete.svg"), RequiresUnlock("Delete the account", Fresh = true)]
[RelayCommand]
private Task DeleteAccountAsync() { … }
```

The attribute on a `[RelayCommand]` method or an `ICommand` property asks wherever Spine runs the command:
- a page action
- a menu or context-menu row
- an action sheet row
- `Tap.Command`
- `{PageCommand}`

`Fresh = true` asks every time, even right after an unlock. Use it for something that cannot be undone.

A plain `Command="{Binding DeleteCommand}"` on a `Button` runs the command itself, so the attribute does not reach it. Use `Unlock.Command` instead:

```xml
<Button Text="Show the code" Unlock.Command="{Binding ShowCodeCommand}" Unlock.Reason="Show the code" />
<Border Unlock.Command="{Binding OpenVaultCommand}" Unlock.Reason="Open the vault"> … </Border>
```

On a `Button` it sets `Command`. On any other view it sets `Tap.Command`, with its press feedback. `Unlock.Fresh="True"` asks every time.

## In code

```csharp
public partial class CardViewModel(IUnlock unlock, ICards cards) : ViewModelBase
{
    [RelayCommand]
    private async Task ShowPinAsync()
    {
        if (await unlock.RequireAsync("Show the PIN"))
            Pin = await cards.GetPinAsync();
    }
}
```

| Member | What it does |
|---|---|
| `IUnlock.RequireAsync(reason, fresh)` | Completes with `true` at once while a recent unlock counts, otherwise after the user unlocks. Calls made while a prompt shows share that prompt. |
| `IUnlock.IsUnlocked` | Whether a recent unlock still counts. |
| `IUnlock.Lock()` | Forgets the unlock, for example on sign-out. |
| `IUnlock.LockChanged` | Raised when the unlock state changes. |
| `IBiometricAuth.GetAvailabilityAsync()` | What the device offers: `Kind` (`FaceId`, `TouchId`, `OpticId`, `Biometric` on Android, `WindowsHello`), `Enrolled` and `HasDeviceCredential`. |
| `IBiometricAuth.AuthenticateAsync(reason, options)` | The bare prompt, without the grace period or messages. Returns `Success`, `Canceled`, `Failed`, `LockedOut`, `NotEnrolled` or `NotAvailable`. |

## How long an unlock counts

One unlock counts for the whole app, for `UnlockValidFor` (one minute by default). It is forgotten when the app goes to the background, or when the desktop window is minimised, so coming back asks again.

| Case | What happens |
|---|---|
| The user cancels | The page does not open and the command does not run. No message. |
| Too many failed attempts | The guard refuses, with a message to unlock the device with its passcode. |
| No passcode and no biometric | With `WhenUnavailable = Allow` (the default) the guard lets it through: there is no lock to ask for. With `Deny` it refuses, with a message asking the user to set up a screen lock. |
| A guarded root page | There is nothing to fall back to, so the [unlock screen](#the-app-lock) covers the app until the user unlocks. |

## The app lock

```csharp
builder.UseSpineBiometrics(o => o.AppLock(a =>
{
    a.LockAfter = TimeSpan.FromMinutes(5);                  // default: zero, every return
    a.LockOnLaunch = true;                                  // default
    a.Reason = "Unlock Orientera";                          // default: "Unlock to continue"
    a.IsEnabled = () => Preferences.Get("AppLock", false);  // the app's own setting; default: always on
}));
```

The whole app sits behind an unlock screen when it starts and when it comes back after `LockAfter` in the background. The screen shows the app's name, the reason and an Unlock button, and asks at once. Cancel leaves the screen with its button. On Android, Back on the screen sends the app to the background.

- **What counts as away.** The time starts when the window stops, not when it goes inactive. The Face ID prompt and Control Center make an iOS app inactive, and they do not lock it. On the desktop only time minimised counts. On Windows a window hidden to the tray also counts.
- **One prompt.** The app lock goes through `IUnlock`, so its unlock counts for `[RequiresUnlock]` for `UnlockValidFor`. A guard that asks while the screen is up shares its prompt.
- **Above everything.** The screen covers the header bar, the tab bar and an open sheet. A sheet that opens while it shows, for example from a notification, goes under it. The page underneath keeps its state.
- **The app switcher.** While the app lock is on:
  - iOS covers the app-switcher snapshot with a blurred cover and the app icon.
  - Android 13 and later keeps the app's thumbnail out of the app switcher. Screenshots still work.
- **No screen lock.** `WhenUnavailable` decides. With `Allow` the screen goes away without asking. With `Deny` it stays, with its message.

### A page that locks again

```csharp
[NavigableRegion(Title = "Statement"), RequiresUnlock("Show the statement", Relock = true)]
public partial class StatementPage { … }
```

With `Relock = true`, the unlock screen covers the page when the app comes back after `LockAfter` while the page is on screen. This works without the app lock too; `LockAfter` is then zero unless `AppLock` sets it. The page is not popped. The screen has a Back button that leaves the page without unlocking, except on a root page.

## A page out of screenshots

```csharp
[NavigableRegion(Title = "Statement"), PrivacyShield]
public partial class StatementPage { … }
```

While a `[PrivacyShield]` page is on screen, whether in the root region, in a sheet or under one, the app does what the platform allows:

| | Screenshots and recordings | App switcher |
|---|---|---|
| iOS | **Cannot be blocked.** `PrivacyShield(CoverWhileCaptured = true)` covers the app while the screen is recorded, mirrored or shared (iOS 17 and later). | A blurred cover with the app icon goes over the window when the app goes inactive |
| Android | Blocked: `FLAG_SECURE` on the activity, an open sheet and Spine's overlays | Blank thumbnail, from `FLAG_SECURE` |
| Windows | Excluded from capture (`WDA_EXCLUDEFROMCAPTURE`, Windows 10 2004 and later) | Not applicable |
| Mac Catalyst | Not blocked | No snapshot to hide |

The shield only goes up while the page is on screen. Spine clears `FLAG_SECURE` and the capture exclusion only where it set them itself, so an app that sets them itself keeps its setting.

## Platforms

| | Prompt | Falls back to | Needs |
|---|---|---|---|
| iOS, iPadOS | Face ID, Touch ID or Optic ID (`LAContext`) | The passcode | `NSFaceIDUsageDescription` in `Info.plist`. The build stops until it is there. Set `<SpineBiometricsCheckFaceIdUsage>false</SpineBiometricsCheckFaceIdUsage>` to skip the check. |
| Mac Catalyst | Touch ID | The Mac password | Nothing |
| Android 6.0+ | `BiometricPrompt` (fingerprint or face, class 2 or better) | PIN, pattern or password | Nothing. The AndroidX Biometric library declares `USE_BIOMETRIC`. |
| Windows | Windows Hello, for the app's own window | The Hello PIN | Nothing |

```xml
<key>NSFaceIDUsageDescription</key>
<string>Unlocks your private pages with Face ID.</string>
```

Without the key, iOS ends an app that asks for Face ID. If the check is skipped and the key is still missing, the unlock returns `NotAvailable` and logs why, instead of the app being ended.

### Testing in the simulator

In the iOS Simulator, turn on **Features › Face ID › Enrolled**. Then use **Matching Face** or **Non-matching Face** when the prompt shows.

On the Android emulator, set a screen lock or enrol a fingerprint in Settings. `adb emu finger touch 1` then touches the sensor.

To see the app lock and the shield:
- **iOS Simulator.** Go home, then swipe up from the bottom edge and hold to open the app switcher. The card shows the cover.
- **Android emulator.** On a `[PrivacyShield]` page, `adb exec-out screencap -p` comes out black. `adb shell dumpsys window windows` lists `SECURE` among the flags of the app's windows. To check the app-switcher thumbnail, go home first and then open the overview. Opened straight from the app, the card shows the app live.

## See also

- [Guards](guards.md): the mechanism behind `[RequiresUnlock]`
- The Biometrics page in Spine Showcase
