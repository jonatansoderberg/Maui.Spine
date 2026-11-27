# Issue #893 — Biometrics.Core: the biometric prompt without Spine's core

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/893
**Branch:** issue/893-biometrics-core
**Status:** Completed

## Plan
An app with its own text sign-in, not on Spine, wants Face ID or a fingerprint to sign in again faster. `Plugin.Maui.Spine.Biometrics` references Spine's core, so the prompt alone cannot be used there.

- New package `Plugin.Maui.Spine.Biometrics.Core` on `.Common` only, like PushNotifications, Images and Speech: `IBiometricAuth`, the three platform implementations, the `Cancel` string, the Face ID usage check, `UseSpineBiometricAuth()` and a `SpineModule` entry.
- `Plugin.Maui.Spine.Biometrics` references it and keeps `IUnlock`, `[RequiresUnlock]`, `Unlock.Command`, the app lock and `[PrivacyShield]`.
- No secret storage: the package only verifies the user (owner's decision).

## Changes
- `IBiometricAuth.cs` and `BiometricAuth.{Apple,Android,Windows}.cs` moved (git mv) to `src/Plugin.Maui.Spine.Biometrics.Core/`; the Android dependency `Xamarin.AndroidX.Biometric` moved with them.
- `build/Plugin.Maui.Spine.Biometrics.targets` (Face ID check) moved to `Plugin.Maui.Spine.Biometrics.Core.targets`; the Showcase imports the new path.
- `BiometricAuthStrings` in Core carries `Spine.Biometrics.Cancel` (en, sv); the key left Biometrics' string files.
- `UseSpineBiometricAuth()` registers `IBiometricAuth` once; `UseSpineBiometrics()` calls it.
- Solution, package filter, test project link, `Directory.Packages.props` comment.
- Docs: README package table, `packages.md` (table, diagram, which package, registration, frameworks, build assets, Android minimum), `biometrics.md` ("Without Spine's core"), the package README, the spine-setup skill, the logo note, the AI and security plan.

## Decisions
- **Namespace kept as `Plugin.Maui.Spine.Biometrics`.** Apps on `.Biometrics` compile unchanged; only the assembly a type lives in changes, so they rebuild against the new version.
- **`BiometricAuth` stays internal**; an app gets it through `UseSpineBiometricAuth()` and DI, like the rest of Spine.
- **`IUnlock` stays in Biometrics.** It does not need the core either, but its options share `SpineBiometricsOptions` with the app lock; the owner asked for verification only.
- **Verification:** a scratch MAUI app (outside the repo) referencing only Core: the build stops without `NSFaceIDUsageDescription`; with it, the iOS Simulator shows the Face ID prompt, a matching face returns `Success`, a non-matching one shows iOS's own retry sheet, and the bundle has no `Plugin.Maui.Spine.dll`.
