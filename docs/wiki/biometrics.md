# Biometrics

`Plugin.Maui.Spine.Biometrics` asks the user to unlock before a page opens or a command runs. Each platform uses its own unlock:

- **iPhone and iPad:** Face ID or Touch ID.
- **Mac:** Touch ID.
- **Apple Vision Pro:** Optic ID.
- **Android:** the fingerprint or face prompt.
- **Windows:** Windows Hello.

Each falls back to the device's passcode, PIN or password.

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
[PageAction(Svg = "trashcan.svg"), RequiresUnlock("Delete the account", Fresh = true)]
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
| A guarded root page | There is nothing to fall back to, so the app keeps asking behind a "Locked" alert until the user unlocks. |

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

## See also

- [Guards](guards.md): the mechanism behind `[RequiresUnlock]`
- The Biometrics page in Spine Showcase
