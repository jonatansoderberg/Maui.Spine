# Issue #521 — Spine.Authentication.Google: Credential Manager on Android, the broker elsewhere

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/521
**Branch:** issue/521-spineauthenticationgoogle-and-spineauthenticationm
**Status:** In Progress

## Plan

Step 5.3a of `docs/plans/ai-and-security.md`. The proposal is `docs/proposals/spine-authentication.md` §3 (Android), §4.2 and §5.4. On 2026-10-15 the owner split #521: this issue is Google plus the provider hook; Microsoft (MSAL, both modes) is #564 (5.3b).

**The provider hook** in `Plugin.Maui.Spine.Authentication` (internal, with `InternalsVisibleTo` for `.Google` and later `.Microsoft`, as `.Common` already does for its siblings):
- An `INativeSignIn` with:
  - `Name`, `DisplayName`, `IsSupported` (the platform has the native sheet);
  - `SignInAsync(nonce, ct)`, which gives an ID token (or a saved password, see below) or null when the user cancels;
  - `SignedOutAsync()`, so Credential Manager forgets the account and does not pick it again by itself.
- `SpineAuthenticationOptions` keeps native providers next to the browser ones. `SpineAuth` routes each name:
  - the native sheet where `IsSupported`;
  - otherwise the server's broker (`/auth/{name}/start`, #557).
  - Apple moves onto the same path, so there is one way a provider is plugged in.
- `SignInAsync()` opens a platform sheet directly when it can show every configured way: Google-only on Android (plus saved passwords). Otherwise the Spine sheet opens, with a "Continue with Google" button.

**`Plugin.Maui.Spine.Authentication.Google`**, `a.AddGoogle(g => g.ServerClientId = "…web client id…")`:
- **Android:** AndroidX Credential Manager (`Xamarin.AndroidX.Credentials` 1.6.0.1, `.Credentials.PlayServicesAuth` 1.6.0.2, `Xamarin.GoogleAndroid.Libraries.Identity.GoogleId` 1.1.0.13).
  - `GetGoogleIdOption` with the server's nonce (`GET /auth/nonce`, not hashed for Google). It tries the accounts that already signed in first, with auto-select, then every account (Google's recommended order).
  - The ID token goes to `POST {Server}/google/token`.
  - When email and password are configured, `GetPasswordOption` rides in the same request, and a saved password signs in through `/auth/password/sign-in`. Saving a password after a sign-in is not in this step.
  - On sign-out, `ClearCredentialStateAsync`.
- **iOS, Mac Catalyst, Windows:** the broker through the browser (#552, #557). There is no native code there: the GoogleSignIn iOS binding is dead.
- **Only with a Spine server.** `AddGoogle` with `UseIdentityProvider` throws at startup.
- **App Store guideline 4.8:** `UseSpine` throws on Apple targets when Google is configured without `AddApple()`, with the guideline in the message. `a.AppleNotRequired = true` turns that off (proposal §5.1).
- **Errors:** Credential Manager's exceptions map to `SignInResult`:
  - `GetCredentialCancellationException` → `Canceled`;
  - `NoCredentialException` → `Failed`, saying no Google account is on the device or none is signed in;
  - any other → `Failed` with the type and message.
- The package's README carries the Google Cloud setup: a Web client (its id is `ServerClientId` and goes in the server's `ClientIds` and `WebClientId`), and an Android client with the package name and the signing SHA-1.

**Server:** nothing new expected. `Google(...)` already validates the tokens (#520), with the web client id in `ClientIds`.

**Showcase:**
- `AddGoogle` behind `-p:SpineSignInWithGoogle=true` (the switch from #557, now the package).
- The push sample server reads `Auth:Google:WebClientId` / `WebClientSecret` (there already) and adds the web client id to `ClientIds`.

**Tests** (`net10.0`): the hook's routing, as far as it is testable without a platform:
- native vs. broker, and the fallback;
- the guideline-4.8 check;
- `AddGoogle` with `UseIdentityProvider` refused.

The Credential Manager code is Android-only and is verified on the emulator.

**Verification:**
- The Android emulator with Google Play and a signed-in Google account: the Showcase signs in with Google through Credential Manager against the push sample server.
- iOS: the same button goes through the broker.
- Windows: compiled only.

**Docs:**
- the wiki (`authentication.md`: Google; a new `authentication-google.md` or a section, whichever reads better);
- the package README with the Google Cloud steps;
- the plan (5.3a/5.3b split, Decisions, backlog), the proposal's status, `Directory.Packages.props`, `Spine.slnx`, and every file the plan lists under "Files every package PR touches".

## Open Questions

- ~~**Google Cloud for verification.** Credential Manager and the broker need a Google Cloud project with:
  - an OAuth Web client (`ServerClientId`, plus its secret for the broker);
  - an Android OAuth client for `se.cosmomedia.spineshowcase` with the debug keystore's SHA-1.

  None exists yet: the push sample server has no `Auth:Google:*` secrets. Do you set them up (I can give the exact steps and the SHA-1), or does this step stop at compiled and unit-tested on Android?~~ Answered 2026-10-15: compiled and unit-tested is enough for this step; the rest goes in the verification backlog.

## Changes

- **The hook** in `Plugin.Maui.Spine.Authentication`:
  - `Core/NativeSignIn.cs` has `INativeSignIn`, `NativeCredential` and `NativeProvider`.
  - `SpineAuthenticationOptions.AddNative` registers the provider and its browser fallback. `IsBrowser` says which way it goes. `AppleNotRequired` and the Guideline 4.8 check go in `Validate(returnsToScheme, appStore)`.
  - `SpineAuthFlows.NativeAsync` takes the server's nonce, opens the sheet and sends the ID token to `/{provider}/token`.
  - `SpineAuth` routes a name to the native sheet where it is supported and to the broker otherwise. It reports `IsNative` per provider, and every sign-out clears the providers' state.
  - `InternalsVisibleTo` for `.Google`.
- **`Plugin.Maui.Spine.Authentication.Google`** (new):
  - `AddGoogle(g => g.ServerClientId = …)` and `SpineGoogleOptions`.
  - `GoogleSignIn.Android.cs`: Credential Manager with `GetGoogleIdOption`, tried first with authorized accounts and auto-select, then with every account. `GetCredentialCancellationException` gives null; `NoCredentialException` gives a message saying so; other errors pass through with their type. Sign-out calls `ClearCredentialStateAsync`.
  - `GoogleSignIn.cs`: the other platforms, with no sheet.
  - The README with the Google Cloud steps.
- `Directory.Packages.props`: AndroidX Credentials 1.5.0.3, PlayServicesAuth 1.5.0.3 and GoogleId 1.1.0.11.
- **`Plugin.Maui.Spine.Authentication/README.md`**, which was missing. `dotnet pack` stopped on NU5019.
- **Tests:** `NativeTests.cs` has 7 facts.
  - Native sign-in against a real `Server.Authentication` with a Google stand-in: success with the server's nonce, cancel, another nonce, another audience.
  - The options: the browser fallback, Guideline 4.8, refused without a server.
- **Sample:**
  - The Showcase references `.Google`, behind `-p:SpineGoogleServerClientId=…`, which replaces `SpineSignInWithGoogle`. That build sets `AppleNotRequired` when Apple is off. The Sign-in page's summary is updated.
  - The push sample server adds the web client to `Google(o => o.ClientIds)` for Android's tokens.
- **Docs:**
  - wiki `authentication.md` (Sign in with Google, a generic `AddOpenIdConnect` example), `packages.md`, `README.md` (thirty-one packages);
  - `spine-setup` SKILL;
  - the plan (5.2b Done, 5.3a/5.3b, backlog, Decisions, history), the proposal's status;
  - `Spine.slnx`, `Spine.Packages.slnf`.

- **Review fixes:** a review agent read the diff before the PR and found no blocking defects. Fixed:
  - a cancelled Credential Manager request never calls back, so the token now also ends the wait;
  - sign-out clears the state through the app context, also without an activity (a refresh refused in the background);
  - one static executor instead of a new thread per call;
  - Android 13 and older without Google Play services (`GetCredentialProviderConfigurationException`) fall back to the broker. An `INativeSignIn` throws `PlatformNotSupportedException` for that, and `CallbackScheme` is now required on Android whenever Google is configured.

## Verification

- **Tests:** `dotnet test` on the client tests gives 44 passed.
- **Builds:**
  - The Google package compiles for Android, iOS, Mac Catalyst and Windows (`EnableWindowsTargeting`).
  - The Showcase builds for Android with Credential Manager, through D8 without duplicate classes, and for the iOS simulator.
  - `dotnet pack` works for `.Authentication` and `.Authentication.Google`. The Google nuspec has the three AndroidX dependencies.
- **iPhone 17 simulator:** the Showcase built with a test client id starts; the guideline check passes with `AppleNotRequired`.
  - The page lists `password, google` (Google not native on iOS).
  - The sheet shows "Continue with Google".
  - Tapping it opens the broker's `/auth/google/start` in the browser. The server was not running, so Safari could not connect, as expected.
- **Not run:** Credential Manager on Android and the broker against Google. There is no Google Cloud project (owner, 2026-10-15), so this is in the plan's verification backlog.

## Decisions

- 2026-10-15, owner: #521 is split into 5.3a Google (this issue, with the provider hook) and 5.3b Microsoft (#564). Microsoft gets both modes: MSAL's ID token to the Spine server, and MSAL's own access token for an Entra API without a server.
- Apple keeps its own path instead of moving onto the hook: it hashes the nonce and has its own button.
- No `GetPasswordOption` in this step: Credential Manager cannot create an account, so Google plus email and password still needs the Spine sheet, and saved passwords help only once the app also saves them.
- AndroidX Credentials 1.5.0.3 (not 1.6): it sits on the AndroidX releases MAUI 10.0.50 ships and Spine pins (Core 1.16.0.3, Biometric 1.1.0.30).
- A device without a Credential Manager provider falls back to the broker instead of failing, so Google works on Fire and Huawei devices too, through the browser.
- The provider hook is internal with `InternalsVisibleTo`, as `.Common` does for its siblings, so there is no public extension API to keep stable before a third provider package asks for one.
