# Haptics

Spine's haptics are semantic. You name what happened, such as a success, a warning, an error, a changed selection or an impact, and the platform decides how it feels. They play through the platform's own generators, so they follow the user's system haptics setting:

- **iOS:** `UINotificationFeedbackGenerator`, `UISelectionFeedbackGenerator` and `UIImpactFeedbackGenerator`.
- **Android:** `View.PerformHapticFeedback`, or `VibrationEffect` if you opt in (see [Android: two engines](#android-two-engines)).
- **Mac Catalyst and Windows:** nothing plays. The calls are safe on these platforms and do nothing.

The haptics are in the core package, and nothing needs to be registered.

## Availability

`Haptics.IsSupported` says whether this device can play haptics at all, so an app can hide a haptics setting where it would do nothing:

| Platform | `Haptics.IsSupported` |
|---|---|
| iOS | `true` on an iPhone whose hardware Core Haptics supports; `false` on an iPad and in the simulator |
| Android | `true` when the device has a vibrator (`Vibrator.HasVibrator`; no permission needed to ask) |
| Mac Catalyst, Windows | `false` |

It is fixed while the app runs. The user's own system haptics or touch-feedback setting does not change it, and the calls stay safe either way.

## From code

```csharp
Haptics.Success();
Haptics.Warning();
Haptics.Error();
Haptics.Selection();
Haptics.Impact(HapticImpact.Light);   // Light, Medium, Heavy, Soft, Rigid

Haptics.Play(Haptic.Rigid);           // the same, from a value
```

| `Haptic` | When to use it |
|---|---|
| `Success` | A task finished: something was saved, sent or registered. |
| `Warning` | A task finished, but with a warning. |
| `Error` | A task failed. |
| `Selection` | A selection changed: a row was picked, a slider hit a stop, a segment switched. |
| `Light`, `Medium`, `Heavy` | Something landed or collided; larger objects take heavier impacts. |
| `Soft`, `Rigid` | An impact that is dull and flexible (`Soft`) or sharp and stiff (`Rigid`). |

The calls can be made from any thread, because Spine moves them to the main thread.

## On a tap

`Haptics.OnTap` plays a haptic on every tap, before the command runs. It works on these views:

- `Button` and `ImageButton`;
- `SpineRow`;
- any view with `Tap.Command`.

```xml
<Button Text="Follow" Command="{Binding FollowCommand}" Haptics.OnTap="Selection" />

<SpineRow Title="Wi-Fi" Command="{Binding OpenCommand}" Haptics.OnTap="Light" />

<Border Tap.Command="{Binding OpenCommand}" Haptics.OnTap="Light">
    ...
</Border>
```

On iOS, Spine prepares the generator as soon as the finger lands, so the haptic plays without a delay. On any other view, `OnTap` does nothing. To play a haptic from a gesture of your own, call `Haptics.Play` in its handler.

## On a header action

```csharp
[PageAction("Save", Role = PageActionRole.Confirm, Haptic = Haptic.Success)]
[RelayCommand]
private Task SaveAsync() { ... }

PageActions.Add(new PageAction("Save", SaveCommand) { Role = PageActionRole.Confirm, Haptic = Haptic.Success });
```

`PageAction.Haptic` is observable, so you can change it while the page is showing.

## Tabs and sheets

Spine can play a haptic on its own in three places. All three are off by default, because the platforms' own tab bars and sheets are silent.

```csharp
builder.UseSpine(options =>
{
    options.Haptics.TabSwitch = Haptic.Selection;
    options.Haptics.SheetDetent = Haptic.Selection;
    options.Haptics.DismissBlocked = Haptic.Warning;
});
```

- `TabSwitch` plays when the user switches tabs. A switch that navigation makes in code plays nothing.
- `SheetDetent` plays when the user drags a sheet to another detent and lets go. It plays nothing if the sheet snaps back to where it was, or if it moves because of code. It works on iOS and Android.
- `DismissBlocked` plays when the user tries to close a sheet and its `OnCloseRequestedAsync` refuses. This covers a swipe down, a tap on the backdrop, the close button and Android's back button, and it works on iOS and Android.
  - It plays only when the guard answers at once, for example with `Task.FromResult(false)`.
  - A guard that awaits, such as one that asks "Discard changes?", plays nothing, because the prompt already tells the user what happened.

## Android: two engines

```csharp
options.Android.HapticEngine = AndroidHapticEngine.Vibrator;
```

| Engine | How it works | Permission | Follows the touch-feedback setting |
|---|---|---|---|
| `View` (default) | `PerformHapticFeedback` on the activity's window. `CONFIRM` and `REJECT` are used on Android 11+, and the nearest constant on earlier versions. | None | Yes |
| `Vibrator` | Composed `VibrationEffect` primitives on Android 11+, predefined effects on Android 10. | `android.permission.VIBRATE` | On Android 13+, through touch usage |

`Vibrator` can play richer patterns: for example, `Success` is two clicks that rise in strength, and `Error` is three strong clicks. It needs the permission in the app's manifest:

```xml
<uses-permission android:name="android.permission.VIBRATE" />
```

Spine falls back to `View` in three cases:

- the permission is missing;
- the device runs Android 9 or earlier;
- the device has no vibrator.

When that happens, Spine writes one warning to logcat under the tag `Spine`.

## Checking that it plays

The simulator and emulators do not vibrate.

- **iOS:** test on a phone.
- **Android:** the vibrator service records everything that played, from either engine, so you can check it on the emulator:

```bash
adb shell dumpsys vibrator_manager
```
