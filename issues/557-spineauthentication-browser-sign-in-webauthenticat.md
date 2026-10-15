# Issue #557 — Spine.Authentication: browser sign-in (WebAuthenticator, Windows loopback) and the mode without a Spine server

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/557
**Branch:** issue/557-spineauthentication-browser-sign-in-webauthenticat
**Status:** In Review

## Plan

Step 5.2b of `docs/plans/ai-and-security.md`. The proposal is `docs/proposals/spine-authentication.md` §4.1, §5.1–§5.3. The owner chose on 2026-10-15 to do both parts in this issue: the browser sign-in through the server's broker (#552), and the mode without a Spine server (Q7).

**The browser layer** (`IBrowserSignIn`, internal):
- **iOS, Mac Catalyst and Android:** MAUI's `WebAuthenticator`, with `PrefersEphemeralWebBrowserSession` on by default (Q10, `EphemeralBrowserSession`).
- **Windows:** an RFC 8252 loopback listener on `127.0.0.1` at a random port, with the default browser opened through `Launcher`. It answers the browser with a short "You can close this window" page. `WebAuthenticator` does not work on Windows in .NET 10.
- **Android:** the package's `SpineAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity` with a fixed Java name. `build/…targets` writes an `AndroidManifestOverlay` with its intent filter for `$(SpineAuthenticationCallbackScheme)` and the Custom Tabs `<queries>` entry, so the app writes no Android code.

**The brokered flow** (with `Server`):
- `AddOpenIdConnect(name, displayName)` for any provider the server brokers (Google through the browser is `AddOpenIdConnect("google", "Google")`).
- `AddApple()` goes through the broker on Android and Windows.
- A PKCE verifier and a state, then `{Server}/{provider}/start` in the browser. The answer's `state` is checked, then `POST /auth/token` with `SpineCodeExchange`.
- `CallbackScheme` gives the mobile redirect (`{scheme}://callback`). Windows uses the loopback, so the server sets `AllowLoopbackRedirects`.

**The mode without a Spine server** (Q7): `a.UseIdentityProvider(o => { o.Authority; o.ClientId; o.Scopes; })` instead of `Server`, against any OpenID Connect provider as a public client with PKCE (Auth0, Entra, Keycloak).
- Discovery; the authorize URL with PKCE, state and nonce; the code exchange; refresh with `offline_access`; revocation when the provider has an endpoint.
- The user comes from the ID token's claims, after checks of `iss`, `aud`, `exp` and the nonce. The token comes straight from the token endpoint over TLS (OIDC Core §3.1.3.7), so its signature is not checked in the app.
- `GetAccessTokenAsync` and the handler hand out the provider's access token.
- It is written in-package, with no dependency: the browser layer and the session are the same.
- The session's refresh is put behind a small interface (`ITokenEndpoint`), implemented by `SpineAuthClient` and the new `IdentityProviderClient`.

**Sheet:** a button per browser provider, and Apple's button on Android and Windows when Apple goes through the broker.

**Tests** (`net10.0`), against stubs:
- the brokered flow: start URL, state check, code exchange against a real `Server.Authentication` broker in the TestServer, with the browser stubbed;
- the mode without a server: discovery, exchange, refresh and the ID-token checks against a stub provider.

**Verification:**
- the Android emulator: the broker flow against the push sample server with the `adb reverse` port;
- the iOS simulator: the WebAuthenticator flow against a stub provider or the broker with a stub provider;
- Windows: compiled only.

**Docs:** the wiki (`authentication.md`), the plan, the proposal, and the sample (an OIDC button against the sample server's broker when configured).

## Open Questions

## Changes

- `Core/BrowserSignIn.cs`: `IBrowserSignIn`, `BrowserAnswer` and `Pkce` (S256 verifier and challenge, random state and nonce).
- `SpineAuthFlows.BrowserAsync(provider, browser)`: the brokered flow, `{Server}{provider}/start` with PKCE and state, the state check, then `SpineAuthClient.CodeAsync` at `POST {Server}token`. `BrowserProblem` maps a closed browser and `access_denied` to `Canceled`, another state or no code to `Failed`.
- `Core/IdentityProviderClient.cs`: the mode without a Spine server. `SpineIdentityProviderOptions` (`Authority`, `ClientId`, `Scopes`, `Name`, `AuthorizeParameters`); cached discovery that is fetched again after a fault; the code exchange; refresh that keeps the old refresh token when the provider sends none and maps `invalid_grant` to 401; RFC 7009 revocation; the user from the ID token after `iss`/`aud`/`exp`/nonce checks.
- `ITokenEndpoint` (refresh, revoke) behind `SpineAuthSession`, implemented by `SpineAuthClient` and `IdentityProviderClient`.
- `SpineAuthenticationOptions`: `CallbackScheme`, `EphemeralBrowserSession` (on), `AddOpenIdConnect(name, displayName)`, `UseIdentityProvider(...)`, and startup checks for both modes.
- `PlatformBrowserSignIn`: `WebAuthenticator` on iOS, Mac Catalyst and Android; an RFC 8252 loopback `HttpListener` and `Launcher` on Windows.
- Android: `SpineAuthenticationCallbackActivity` with a fixed Java name, and `_SpineAuthenticationAndroidManifest` in the build targets, which writes a manifest overlay (intent filter for `{scheme}://callback`, Custom Tabs `<queries>`) from `<SpineAuthenticationCallbackScheme>`.
- `SpineAuth`: browser providers, Apple through the broker where there is no native sheet, the direct mode, and `Failed` for the password and delete methods without a server.
- `SignInSheet`: a "Continue with {0}" button per browser provider (English and Swedish).
- Tests (`BrowserTests.cs`, 21 facts): the brokered flow against a real `Server.Authentication` broker in a TestServer with a scripted browser and a Google stand-in; the mode without a server against a stub provider.
- Sample: `CallbackScheme` and `<SpineAuthenticationCallbackScheme>`, Google behind `-p:SpineSignInWithGoogle=true`, and `GET /sample/me` no longer crashes the app when the push sample server is down.
- Docs: wiki `authentication.md` (Browser sign-in, Without a Spine server), the plan, the proposal's status, the server wiki's link.

- Review fixes (a review agent read the diff before the PR): the Windows loopback is a `TcpListener` on port 0 that answers 404 to requests without a `state` (a favicon) and gives up after five minutes; `https` required for the authority and the token endpoint (http on loopback only) and the discovery issuer must match; `CallbackScheme` required at startup where a browser sign-in returns to it, and lowercase; the authorize URL keeps its escaping (`AbsoluteUri`); a non-string `aud` is another client, not an exception; a refresh that times out or loses its connection keeps the session (`SpineAuthSession`). Six more facts (37 in all).

## Verification

- `dotnet test` on the client tests (37) and the server tests (83 passed, 8 skipped), three runs of the client tests.
- Compiled for net10.0, iOS, Android, Mac Catalyst and Windows (`EnableWindowsTargeting`).
- iPhone 17 simulator (iOS 26.4), the Showcase against a local OIDC stub in the mode without a server: cancel at the provider → `Canceled`; sign-in (PKCE and redirect checked by the stub, user from the ID token); a restart that kept the session; a refresh after the access token ran out; sign-out revoking the latest refresh token.
- Android: the merged manifest has the activity with `scheme` + `host="callback"` and the Custom Tabs query. Not run: the Pixel 10 Pro emulator was in use by another session and the tablet had no room for the APK.
- Not run: Google or Apple through the broker against the real providers (no Google web client is configured), the Windows loopback, Mac Catalyst.

## Decisions

- 2026-10-15, owner: the browser sign-in and the mode without a server are both in #557.
- The OIDC client is written in-package rather than taking `Duende.IdentityModel.OidcClient`: the browser layer and the session are shared, and the protocol needed is small (discovery, PKCE, code, refresh).
- The ID token's signature is not checked in the mode without a server, only its claims: it comes from the token endpoint over TLS (OIDC Core §3.1.3.7).
- The Windows loopback is a raw `TcpListener`, not `HttpListener`: http.sys refuses `http://127.0.0.1:port/` without a URL ACL, and the server accepts only IP literals. A closed tab is noticed only by the five-minute limit.
- The Android intent filter takes only the host `callback`: the Showcase already uses its scheme for `…://widget` links, and a scheme-only filter put a chooser in front of them.
- Brokered providers are added by name (`AddOpenIdConnect`), since the server brokers any OIDC provider by name; #521 can add `AddGoogle()` on top.
