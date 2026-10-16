# Issue #564 — Spine.Authentication.Microsoft: MSAL with the broker, via the Spine server or directly

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/564
**Branch:** issue/564-spineauthenticationmicrosoft-msal-with-the-broker-v
**Status:** In Review

## Plan

Step 5.3b of `docs/plans/ai-and-security.md`. Proposal: `docs/proposals/spine-authentication.md` §5.4. On 2026-10-15 the owner split it from #521 and chose:
- both modes;
- `AddMicrosoft` + `UseMicrosoft` as the API;
- the broker where it is installed;
- verification against an app registration in the owner's tenant.

**`Plugin.Maui.Spine.Authentication.Microsoft`** (new), MSAL.NET `Microsoft.Identity.Client` 4.90.1, plus `.Broker` and `.Extensions.Msal` on Windows. 4.91.0 is three days old.
- **`a.AddMicrosoft(m => { m.ClientId = …; m.Tenant = "common"; m.Scopes = […]; })`**, next to `Server`. MSAL's ID token goes to `POST {Server}/microsoft/token`, which the server checks already (#520; Microsoft needs no nonce, because MSAL picks its own). It plugs into the provider hook from #521 (`AddNative`): "Continue with Microsoft" in the sheet, and the sheet opens directly when Microsoft is the only way to sign in.
- **`a.UseMicrosoft(m => …)`** in place of `Server` (Q7, like `UseIdentityProvider`). `GetAccessTokenAsync` and the handler hand out MSAL's own access token for `m.Scopes` (an Entra-protected API).
  - The session's `ITokenEndpoint` is MSAL. The "refresh token" the session keeps is the MSAL account's id. Refresh is `AcquireTokenSilent`: `MsalUiRequiredException` signs out, and offline keeps the session. Revocation is `RemoveAsync(account)`.
  - The core gets a small generalization: the direct mode becomes an internal `IDirectSignIn` (sign in + `ITokenEndpoint`), implemented by `IdentityProviderClient` and by MSAL, instead of being tied to `UseIdentityProvider`.
- **Per platform:**
  - **iOS:** MSAL with `WithBroker()` (Microsoft Authenticator, or the system browser when it is not installed). The redirect is `msauth.{bundle id}://auth`.
    - **Keychain:** the keychain group `com.microsoft.adalcache` as an entitlement (`SpineEntitlement`), with the app's own group first. With the MSAL group alone, it would become the default group, and the app's existing SecureStorage items (Spine's refresh token among them) would no longer be found.
    - **Info.plist:** the URL scheme and `LSApplicationQueriesSchemes` (`msauthv2`, `msauthv3`) go in the app's own Info.plist. The build stops with a message when they are missing, as Biometrics does for Face ID.
    - **Broker answers:** `OpenUrl` passes them to `AuthenticationContinuationHelper` through MAUI's lifecycle events.
  - **Android:** MSAL with the broker (Authenticator / Company Portal), or Custom Tabs.
    - **Redirect:** `msauth://{package}/{signature hash}`, with the hash read from the package's signature at run time, so the app needs no code for it. The hash still has to be registered in Entra.
    - **Manifest:** MSAL's `BrowserTabActivity` with its intent filter, and `<queries>` for the brokers and Custom Tabs, as a manifest overlay from the build targets.
    - **Activity results:** forwarded through the lifecycle events.
  - **Windows:** WAM (`Microsoft.Identity.Client.Broker`) with the window handle. Redirect `ms-appx-web://microsoft.aad.brokerplugin/{client id}`. MSAL's cache is persisted with `Microsoft.Identity.Client.Extensions.Msal` (DPAPI).
  - **Mac Catalyst:** MSAL has no Catalyst build, so it runs its `net8.0` build with a custom web UI (`ICustomWebUi`) over Spine's browser layer (`WebAuthenticator`). The redirect is `{CallbackScheme}://callback`. MSAL's cache is kept in SecureStorage (the Keychain).
- **App Store Guideline 4.8** applies as for Google (`AppleNotRequired`); `UseMicrosoft` without `AddApple` is checked too.
- **README:** the Entra steps for each platform's redirect URI and how to get the Android signature hash.

**Tests** (`net10.0`): the MSAL parts are platform-bound, so the tests cover:
- the direct mode's generalization (`IdentityProviderClient` still works through `IDirectSignIn`);
- the session with a fake `IDirectSignIn` (silent refresh, a refusal that signs out, offline);
- the options: `AddMicrosoft` with/without a server, `UseMicrosoft` with `Server` refused, Guideline 4.8.

**Verification** with the app registration "Spine Showcase sign-in" in the owner's tenant (jonatancosmomedia.onmicrosoft.com):
- Accounts in any tenant and personal Microsoft accounts.
- Public client redirects for iOS, Android, Mac Catalyst and Windows.
- I create it with `az`, in an isolated config dir with a device code the owner approves. The owner signs in in the simulator; I never type a password.
- **iOS simulator** (no Authenticator, so the system browser), in both modes:
  - `AddMicrosoft` against the push sample server, with `Microsoft(o => o.ClientId)` on the server;
  - `UseMicrosoft` with Graph `User.Read` as the scope.
- **Android emulator:** if one is free and has room; otherwise compiled.
- **Windows:** compiled.

**Showcase:** `-p:SpineMicrosoftClientId=…` turns on `AddMicrosoft`, as Google does. The direct mode only in a scratch build.

## Open Questions

## Changes

- **Core** (`Plugin.Maui.Spine.Authentication`):
  - **Direct mode:** the mode without a server is now `IDirectSignIn` + `DirectProvider`. `UseIdentityProvider` goes through it too.
  - **Hooks:** `INativeSignIn` gained `UsesNonce`, `RefusedAsync` and `PictureAsync`. `NativeProvider` gained `UsesBrowser` and `FallsBackToBrowser`.
  - **Picture:** `ISpineAuth.GetPictureAsync` with `PictureCache` (a day on disk, cleared at sign-out).
  - **Sign-out:** waits for the providers to forget the account.
  - **Apple:** first and last name are sent separately.
- **`Plugin.Maui.Spine.Authentication.Microsoft`** (new):
  - **API:** `AddMicrosoft`, `UseMicrosoft` and `SpineMicrosoftOptions` (`ClientId`, `Tenant`, `Scopes`, `UseBroker`, `RedirectUri`, `Picture`).
  - **`MicrosoftClient`** sets MSAL up per platform: the iOS keychain group and broker, Android's signature-hash redirect, WAM with a DPAPI cache on Windows, and on Mac Catalyst a custom web UI over Spine's browser with a SecureStorage cache. It asks for `User.Read` and `email`, and fetches Graph's picture.
  - **`MicrosoftNativeSignIn`** (via the server, no nonce) and **`MicrosoftDirectSignIn`** (MSAL's tokens, with a forced renewal after a 401).
  - **Android:** `SpineMicrosoftCallbackActivity`, plus the build targets' manifest overlay.
  - **iOS:** the build checks the app's Info.plist.
  - **`SpineModule`:** passes MSAL the answers from `OpenUrl` and `OnActivityResult`.
  - The README with the Entra setup.
- **Server** (`Server.Authentication`):
  - `SpineAuthUser` and the Azure Tables row gain `PictureUrl`, `GivenName`, `FamilyName` and `Organization`.
  - The ID token's `picture` (https), `given_name`, `family_name`, `hd`/`tid` and `xms_edov` are read.
  - A Microsoft email is kept when its domain is verified.
  - `SignInMethods` comes from the logins at token issue.
- **Common:** `SpineUser` gains `PictureUrl`, `GivenName`, `FamilyName`, `Organization` and `SignInMethods`. `SpineIdTokenRequest` gains `GivenName` and `FamilyName`.
- **Tests:**
  - Client, now 60: `DirectTests` (7), `PictureTests` (8), `NativeTests` (+2: no nonce for Microsoft, a refused account forgotten), and the mailbox race fixed.
  - Server, now 88: the picture (3), names, workspace domain, Microsoft's verified email, and `SignInMethods` on linking.
- **Sample:** `-p:SpineMicrosoftClientId`, the Info.plist entries, and the push server's `Auth:Microsoft:*`. The Sign-in page shows the picture, the names, the organization and the sign-in methods.
- **Entra:** the registration "Spine Showcase sign-in" (client id `08c05546-…`) in the owner's tenant, made with az. It has redirects for iOS, Android, Mac Catalyst and Windows, and the optional claims `email`, `xms_edov`, `given_name` and `family_name`.
- **Docs:** wiki `authentication.md` (Sign in with Microsoft, the user's details), `authentication-server.md`, `packages.md`, `README.md` (thirty-two packages), the `spine-setup` skill, the plan, the proposal's status.

## Verification

- **Tests:** `dotnet test` gives 60/60 client and 88 server facts (8 skipped).
- **Builds:** the package compiles for Android, iOS and Mac Catalyst. For Windows, the C# compiles on the Mac once `ResolveNativeReferences` is turned off; the WAM package's NativeReference applies only on a Mac build host.
- **iPhone 17 simulator (no Authenticator, so the system browser), against the Entra registration with the owner's personal account:**
  - **`AddMicrosoft`:**
    - sign-in through the push sample server, and `GET /sample/me` 200;
    - a second sign-in silent;
    - sign-out clears MSAL (the next sign-in asks);
    - the picture through Graph, gone with the cache at sign-out;
    - first and last name;
    - the organization null for a personal account;
    - `SignInMethods` = `microsoft`;
    - the email null (no `xms_edov` for personal accounts).
  - **`UseMicrosoft`** (a scratch build): sign-in, Graph `/me` and `/me/photo` (768×768 JPEG) with the session's token, and a restart with silent renewal.
- **Not run:** Android, Windows, Mac Catalyst, Authenticator as broker, and a work account. These are in the plan's backlog.

## Decisions

- 2026-10-15, owner: both modes; `AddMicrosoft` (via the Spine server) and `UseMicrosoft` (directly) as the API; the broker where it is installed; verification against an app registration in the owner's tenant, created by Claude with az through a device code the owner approves.
- MSAL 4.90.1 rather than 4.91.0, which was published on 2026-10-12.
