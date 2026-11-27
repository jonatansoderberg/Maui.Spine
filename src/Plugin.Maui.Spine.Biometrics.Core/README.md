# Plugin.Maui.Spine.Biometrics.Core

The platform's biometric prompt for .NET MAUI, without Spine's core: Face ID, Touch ID or Optic ID on Apple platforms, the fingerprint or face prompt on Android, Windows Hello on Windows. Each falls back to the device passcode unless `AllowDeviceCredential` is false. It depends on `Plugin.Maui.Spine.Common` only, so an app that does not run Spine can use it.

```bash
dotnet add package Plugin.Maui.Spine.Biometrics.Core
```

```csharp
builder.UseMauiApp<App>().UseSpineBiometricAuth();   // UseSpine() registers it in a Spine app
```

```csharp
public class LoginViewModel(IBiometricAuth biometrics)
{
    async Task SignInAgainAsync()
    {
        if (!(await biometrics.GetAvailabilityAsync()).CanAuthenticate)
            return;   // show the password form

        if (await biometrics.AuthenticateAsync("Sign in to your account") == BiometricResult.Success)
        {
            // the user is verified: sign in with what the app kept
        }
    }
}
```

An unlock protects the user interface, not data at rest: the package only verifies the user, and the app keeps its own credentials. `[RequiresUnlock]`, an unlock that counts for a minute, the app lock and `[PrivacyShield]` are in `Plugin.Maui.Spine.Biometrics`, which builds on this package and needs Spine's core.

**Face ID usage text:** an iOS app needs `NSFaceIDUsageDescription` in its `Info.plist`. The build stops with a message until it is there; set `SpineBiometricsCheckFaceIdUsage` to `false` to skip the check.

## Documentation

- [Biometrics](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/biometrics.md)
