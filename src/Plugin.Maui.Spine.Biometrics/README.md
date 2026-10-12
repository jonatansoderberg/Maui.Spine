# Plugin.Maui.Spine.Biometrics

Face ID, Touch ID, Android biometrics and Windows Hello for .NET MAUI. `[RequiresUnlock]` on a Spine page or command asks the user to unlock before the page opens or the command runs. One unlock counts for a minute, and the app forgets it when it goes to the background.

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

[PageAction(Svg = "trashcan.svg"), RequiresUnlock("Delete the account", Fresh = true)]
[RelayCommand]
private Task DeleteAccountAsync() { … }

if (await unlock.RequireAsync("Show the PIN")) { … }   // IUnlock, in code
```

```xml
<Button Text="Show the code" Unlock.Command="{Binding ShowCodeCommand}" Unlock.Reason="Show the code" />
```

**Face ID usage text:** an iOS app needs `NSFaceIDUsageDescription` in its `Info.plist`. The build stops with a message until it is there.

## Documentation

- [Biometrics](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/biometrics.md)
- [Guards](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/guards.md)
