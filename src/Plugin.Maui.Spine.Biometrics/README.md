# Plugin.Maui.Spine.Biometrics

Face ID, Touch ID, Android biometrics and Windows Hello for .NET MAUI. `[RequiresUnlock]` on a Spine page or command asks the user to unlock before the page opens or the command runs. One unlock counts for a minute, and the app forgets it when it goes to the background. An app lock covers the whole app at launch and on return, and `[PrivacyShield]` keeps a page out of screenshots and the app switcher where the platform allows it.

```bash
dotnet add package Plugin.Maui.Spine.Biometrics
```

```csharp
builder.UseSpine(options => options.AddAssembly(typeof(MauiProgram).Assembly));   // registers Biometrics too
// builder.UseSpineBiometrics(o => o.UnlockValidFor = TimeSpan.FromMinutes(5));   // only to change the options
```

```csharp
[NavigableRegion(Title = "PIN"), RequiresUnlock("Show the PIN")]
public partial class PinPage { … }

[PageAction(Svg = "delete.svg"), RequiresUnlock("Delete the account", Fresh = true)]
[RelayCommand]
private Task DeleteAccountAsync() { … }

if (await unlock.RequireAsync("Show the PIN")) { … }   // IUnlock, in code

[NavigableRegion(Title = "Statement"), PrivacyShield, RequiresUnlock("Show the statement", Relock = true)]
public partial class StatementPage { … }
```

```csharp
builder.UseSpineBiometrics(o => o.AppLock(a => a.LockAfter = TimeSpan.FromMinutes(5)));
```

**iOS cannot block screenshots.** `[PrivacyShield]` covers the app-switcher snapshot there. Android blocks capture with `FLAG_SECURE`, and Windows excludes the window from capture.

```xml
<Button Text="Show the code" Unlock.Command="{Binding ShowCodeCommand}" Unlock.Reason="Show the code" />
```

**Face ID usage text:** an iOS app needs `NSFaceIDUsageDescription` in its `Info.plist`. The build stops with a message until it is there.

## Documentation

- [Biometrics](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/biometrics.md)
- [Guards](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/guards.md)
