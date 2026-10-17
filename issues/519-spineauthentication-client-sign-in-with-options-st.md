# Issue #519 — Spine.Authentication: client sign-in with options-style providers and [RequiresSignIn]

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/519
**Branch:** issue/519-spineauthentication-client-sign-in-with-options-st
**Status:** In Progress

## Plan

Step 5.2a of `docs/plans/ai-and-security.md`. The proposal is `docs/proposals/spine-authentication.md` §5.1–§5.3 and §5.6. The owner decided on 2026-10-15:
- **Split:** this is 5.2a. The browser flows (`WebAuthenticator`, the Windows loopback, the Android callback activity) and the mode without a Spine server (Q7) are 5.2b (#557).
- **Registration** stays as Q8 says, `o.AddAuthentication(...)` inside `UseSpine`, even though every package since has used `UseSpineX(...)`. The core gets a small general hook for that.

**Core (`Plugin.Maui.Spine`):** `SpineOptions.Configure<TOptions>(Action<TOptions>)` keeps a package's settings, and `SpineOptions.ApplyConfiguration<TOptions>(TOptions)` hands them to the package once. `UseSpine` runs `configure` before the modules (`SpineModules.Run`), so a package's module registration finds them. The auth package adds `o.AddAuthentication(a => …)` as an extension on `SpineOptions`.

**New package `Plugin.Maui.Spine.Authentication`** (the four MAUI frameworks):
- **`SpineAuthenticationOptions`:**
  - `Server` (the URI of `MapSpineAuthentication`'s prefix);
  - `AddApple()` (native on iOS and Mac Catalyst; Android and Windows come with #557);
  - `AddEmailPassword(e => …)`;
  - `LinkPushNotifications` (opt-in, Q11);
  - `SignInPage<TPage>()` for an app's own sheet.
- **`ISpineAuth`:**
  - State: `User`, `IsSignedIn`, `Providers`.
  - Sign-in: `SignInAsync()` (the native Apple sheet when Apple is the only method on an Apple platform, otherwise the package's `SignInSheet`) and `SignInAsync(provider)`.
  - Email and password for custom UIs: `SignInWithPasswordAsync`, `SignUpAsync`, `VerifyEmailAsync`, `ResendCodeAsync`, `ForgotPasswordAsync`, `ResetPasswordAsync`.
  - Tokens and the account: `GetAccessTokenAsync`, `SignOutAsync`, `DeleteAccountAsync`.
  - Events: `SignedIn`, `SignedOut`.
  - The answers are `SignInResult(Status, User, Error)`, where `Error` carries the path and status when the server refused, so a misconfiguration is visible.
- **Tokens:**
  - The refresh token and the user are kept in `SecureStorage`, and the access token in memory only. `GetAccessTokenAsync` refreshes when under a minute is left, one refresh at a time.
  - A refused refresh signs the user out and raises `SignedOut`.
  - Sign-out revokes on the server.
- **`IHttpClientBuilder.AddSpineAuthentication()`:** a `DelegatingHandler` that adds the bearer token and refreshes and retries once on 401. `ISpineAuth.CreateHandler()` covers apps without `IHttpClientFactory`.
- **`[RequiresSignIn]`:** a `GuardAttribute` (`Order = 100`), with a guard and an `IGuardCover`, as `UnlockGuard` does. The sign-in sheet opens first; when the user signs in, the navigation or command goes on.
- **`SignInSheet`:** a `[NavigableSheet]` that returns `SignInResult`.
  - Contents: the Apple button (on Apple platforms), then email and password with the platform's autofill types. Steps for sign-in, sign-up, the code, and the forgotten password. Cancel is the header X (`PageActionRole.Cancel`).
  - Strings in EN and SV via embedded XML (`Spine.Authentication.*`).
- **Apple native:** `ASAuthorizationController` with a nonce from `GET /auth/nonce`, sent to Apple as SHA-256 hex. The identity token, the authorization code and the first-time name go to `POST /auth/apple/token`. The entitlement comes from MSBuild `SpineSignInWithApple=true` (a `SpineEntitlement`), never unconditionally, because the App ID needs the capability.
- **Push link:**
  - `ISignedInSession` in `.Common`, with the user id, the authorization header and `Changed`.
  - When `LinkPushNotifications` is on, PushNotifications uses it if its own `AuthorizationHeader` is unset, and re-registers on `Changed`. The server's `UserId` (#520) then tags the device.
- **Showcase:**
  - A "Sign-in" sample page with a `[RequiresSignIn]` page behind it, and a protected call to `/sample/me` through the handler.
  - The sample server (the push sample server) already serves `/auth`.
  - `LoginSheetPage` stays as the sheet sample.
- **Tests:** `tests/Plugin.Maui.Spine.Authentication.Tests` (`net10.0` parts) for the token store logic, refresh in flight, the handler's retry, and sign-out on a refused refresh, against a fake server.
- **Docs:** `docs/wiki/authentication.md`, the package rows and counts, the spine-setup skill, the plan, and the proposal.

## Open Questions

## Changes

- **Core:** `SpineOptions.Configure<TOptions>` and `ApplyConfiguration<TOptions>`, the hook for package settings inside `UseSpine`.
- **`.Common`:** `ISignedInSession`.
- **New package `Plugin.Maui.Spine.Authentication`:**
  - `net10.0` core: `SpineAuthenticationOptions`, `SignInResult`, `ISpineAuth`, `SpineAuthClient`, `SpineAuthSession` (single-flight refresh, save-then-use, startup load), `SpineAuthFlows`, and the `SpineAuthHandler` with `AddSpineAuthentication()`.
  - MAUI: `o.AddAuthentication(…)` and `UseSpineAuthentication`, `SpineAuth`, `SecureSpineAuthStorage`, `AppleSignIn` (`ASAuthorizationController`), `[RequiresSignIn]` with its guard and cover, `SignInSheet` with its view model, EN and SV strings.
  - Build files: `SpineModule` props, the `SpineSignInWithApple` entitlement targets, the icon `authentication.png`.
- **PushNotifications:** the registration uses `ISignedInSession`'s header when the app sets none, registers again on `Changed`, and has the user in its fingerprint.
- **Server.Authentication:** `RefreshReuseGrace` (30 s), from the review.
- **Tests:**
  - `Plugin.Maui.Spine.Authentication.Tests`: 16 facts against a real `Server.Authentication` in a TestServer.
  - Server.Authentication: a test for the grace window, and the reuse tests now step past it.
- **Showcase:** the Sign-in and Members pages, `o.AddAuthentication` and the `SampleApi` client in `MauiProgram`, and `SPINE_SIGN_IN_WITH_APPLE` from `-p:SpineSignInWithApple=true`.
- **Docs:** `docs/wiki/authentication.md`; the README and `packages.md` (count, tables, graph, frameworks, modules, build steps); the spine-setup skill; the server wiki's grace window; the plan; the proposal's status; `assets/logo-src/README.md`.
- **Build files:** `Spine.slnx`, `Spine.Packages.slnf` and `Directory.Packages.props` (Microsoft.Extensions.Http 10.0.0).

## Decisions

- 2026-10-15, owner: split into 5.2a (#519) and 5.2b (#557); registration through `o.AddAuthentication(...)` in `UseSpine` (Q8 kept).
- See the plan's Decisions for 2026-10-15 (#519): the core hook, the TestServer tests, the grace window, save-then-use, the binding context of code-built content, and `{PageCommand}` for guarded commands.
- **Found in the simulator and fixed:**
  - Code-built content had no binding context: Apple's button showed empty, and every field showed at once.
  - The sheet had two X buttons, because Spine adds its own and `SignInSheet` had a Cancel action too; the Cancel action is gone.
  - A restart showed "Signed out" until a token was asked for. The session now loads at startup, and `UserChanged` tells the UI.
  - The Showcase's plain `{Binding}` ran the guarded command without asking. It now uses `{PageCommand}`.
- **Review before the PR.** An agent reviewed the client, and every finding was checked and fixed:
  - a lost refresh answer revoking the sign-in (client timeout, plus the server's grace);
  - tokens used before they were saved;
  - a Keychain read error clearing the session;
  - network errors crashing the sheet;
  - push staying on the old user after a sign-out that failed to reach the server (the user is now in the fingerprint);
  - the retry after a 401 with a body that could be read only once (now buffered);
  - the shared sheet using the first caller's cancellation;
  - an unsafe lazy load;
  - Apple's window lookup, and a canceled Apple request now closing Apple's sheet;
  - a sign-in on top of another (the old one is revoked);
  - registration before `UseSpine`.
- **Mutation check:** with the stale-token check in the refresh removed, `Parallel_callers_share_one_refresh` failed. The check was then put back.
- **Verified locally** (CI is paused):
  - `dotnet test`: Authentication (client) 16 passed, and Server.Authentication 83 passed (8 Azurite facts skipped without Docker).
  - The package builds for net10.0, iOS, Android and Mac Catalyst, and the Showcase for the iOS simulator.
  - iPhone 17 Pro simulator against the push sample server: Open Members opened the sheet; Create account, then the code from the server's log; a wrong (empty) code showed its error; the Members page opened after the sign-in; `GET /sample/me` through the handler answered 200 with the user; a restart kept the session; Sign out; the guarded command opened the sheet, and closing it left the command unrun.
  - Not verified: Sign in with Apple's sheet (the Showcase's App ID lacks the capability), Android, Windows and Mac Catalyst at run time, and the push link with a real token.
