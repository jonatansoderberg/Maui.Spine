# Issue #552 — Spine.Server.Authentication: browser broker for Apple web, Google web and OpenID Connect

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/552
**Branch:** issue/552-spineserverauthentication-browser-broker-for-apple
**Status:** In Progress

## Plan

Step 5.1b of `docs/plans/ai-and-security.md`. It was split from #520 by the owner on 2026-10-15. The design is proposal `docs/proposals/spine-authentication.md` §4.1 B and §5.5. The server is the confidential OpenID Connect client, and the app only ever sees a one-time code that it trades with PKCE (RFC 8252).

**Endpoints** in `MapSpineAuthentication`, and as static handlers for Azure Functions:
- **`GET /auth/{provider}/start?redirect_uri=&code_challenge=&code_challenge_method=S256&state=`**
  - The app's redirect must be a configured custom scheme (`RedirectSchemes`) or an `http` loopback (`127.0.0.1`/`[::1]`, any port, RFC 8252 §7.3).
  - The server keeps the app's redirect, state and challenge in the one-time store under its own state, with its own nonce (and its own PKCE verifier toward the provider where the provider takes one). It then redirects to the provider's `authorization_endpoint` from discovery.
- **`GET`/`POST /auth/{provider}/callback`**
  - It takes the server state once and exchanges the code at the provider's `token_endpoint` as a confidential client. For Apple, the client secret is the ES256 JWT with the Services ID as `sub`.
  - It validates the ID token exactly as a native one, nonce included.
  - It signs the user in with the same user and linking rules, and keeps Apple's refresh token from this exchange for revocation. Apple's first-time `user` JSON supplies the name.
  - It then redirects to the app's redirect with a one-time `code` (2 minutes) and the app's `state`, or with `error` and `state`.
- **`POST /auth/token` with `{ code, codeVerifier, redirectUri }`** (`SpineCodeExchange` in `.Common`): the code is taken once, the redirect must match, and the SHA-256 of the verifier must equal the challenge. The answer is `SpineTokens`. The same route still takes `{ refreshToken }`.

**Configuration:**
- `SpineAuthOptions.PublicUrl`: the browser-facing URL of the auth endpoints, for the provider's `redirect_uri`. The `Host` header is not trusted.
- `RedirectSchemes`.
- Per provider: `WebClientId`, `WebClientSecret`, `Scopes`, `FormPost` and `UsePkce`.
  - Apple: `ServicesId`, scopes `name email`, `form_post`, no PKCE (Apple does not document it), and a secret from `TeamId`/`KeyId`/`PrivateKey`.
  - Google and OIDC: `openid email profile` with PKCE.
- `Validate()` refuses a broker provider without a secret (or Apple keys), or without `PublicUrl`.

**Refactor:** `SpineAccounts.SignInWithIdTokenAsync` is split, so that the broker reuses the user step and issues a code instead of tokens.

**Tests:** a stub provider (fake authorize URL, token endpoint through the named `HttpClient`, ID tokens from `FakeIssuer`):
- start and its redirect parameters;
- refused redirects and challenge methods;
- the callback through GET and Apple's form_post, with the name from `user`;
- the state used once, provider errors passed to the app, and a code exchange that checks the verifier, the redirect and single use;
- the Apple refresh token kept from the exchange.

**Docs:** the wiki, the push sample server (`send.http` with a loopback flow), the plan (5.1a Done, 5.1b status, Decisions), the proposal's status.

## Open Questions

## Changes

- **`SpineBroker` (new):**
  - `StartAsync`: checks the redirect, the S256 challenge and the state length; stores the pending sign-in; redirects to the provider.
  - `CallbackAsync`: query or form; takes the state once; exchanges the code as a confidential client; signs the user in; issues a two-minute code.
  - `RedeemAsync`: the code, the redirect and the PKCE verifier.
- **`SpineAuthOptions`:**
  - `PublicUrl`, `RedirectSchemes`, and their checks in `Validate()`.
  - Per provider: `WebClientId`, `WebClientSecret`, `Scopes`, `FormPost`, `UsePkce`.
  - Apple's `ServicesId` and its defaults.
- **`IdTokenValidator`:** `Sent` (the nonce as the provider gets it), `ConfigurationAsync`, `SaveNonceAsync`. Audiences include the web client.
- **`SpineAccounts`:** `SignInUserAsync` split out of `SignInWithIdTokenAsync`, with a provider refresh token the caller already has.
- **`SpineAuthEndpoints`:** `StartAsync`, `CallbackAsync` (`GET`/`POST`), and `RefreshAsync` that also takes a `SpineCodeExchange`. All mapped in `MapSpineAuthentication`.
- **`AppleTokenClient`** is registered once, when Apple has its keys, and shared by accounts and the broker.
- **`.Common`:** `SpineCodeExchange`.
- **Tests:** `BrokerTests` covers start, Google through the browser and back, the code's checks, expiry, the state used once and tied to its provider, cancel, provider refusal, a foreign nonce, redirects (refused and loopback), S256 only, a provider without a web client, and Apple's form_post with the name and the refresh token. `BrokerSetupTests` covers `PublicUrl`, the secret, Apple's keys and the redirect schemes.
- **Push sample server:** `PublicUrl`, the Showcase's scheme, Apple's and Google's browser sign-in from user secrets, and `send.http` steps with a loopback redirect.
- **Docs:** the wiki's "Browser sign-in", the endpoint table, Functions and the sample; the plan (5.1a Done, 5.1b in review, Decisions, verification backlog, history); the proposal's status.

## Decisions

- See the plan's Decisions for 2026-10-15 (#552): one package with the native user step reused, the nonce inside the single-use state, the code at `POST /auth/token`, the redirect rules, Apple without PKCE, and error text kept in the log.
- **Review before the PR.** An agent read the broker and found nothing critical. Every finding was checked and fixed with a test:
  - loopback is opt-in (`AllowLoopbackRedirects`, RFC 8252 §8.6);
  - redirects are kept as `AbsoluteUri`, and one carrying `code`, `state` or `error` is refused;
  - the web client's audience is accepted only from the broker's own exchange;
  - Apple's `user` is read only from Apple's form, at most 1 KB, with a small form limit;
  - malformed bodies, forms and keys no longer give 500;
  - discovery failures answer `provider_unavailable`;
  - the provider's error text is sanitized in the log.
- **Flaky tests from #553 fixed.** #553 sends emails without the request waiting, and its password tests looked at the recording sender at once. With Azurite running, about one run in eight failed. `SpineAccounts.EmailsSentAsync()` (internal) lets each test step wait for its emails; 12 runs in a row passed.
- **Mutation check.** With the PKCE comparison, the redirect comparison and the scheme check removed, three tests failed. The checks were then put back.
- **Verified locally** (CI is paused):
  - `dotnet test` with Azurite in Docker: Authentication 90 passed (12 runs in a row), Server 264 and Server.AI 81. The push sample server builds.
  - Live: the push sample server with placeholder Apple (Services ID, a generated key) and Google web clients. `/auth/apple/start` and `/auth/google/start` led through Apple's and Google's real discovery documents to `https://appleid.apple.com/auth/authorize` (200) and `https://accounts.google.com/o/oauth2/v2/auth` (302). A callback with an unknown state showed the expired page.
  - Not verified: a whole browser sign-in with real web clients (it needs a Services ID with a public return URL and a Google web client), and Azure Functions. Both are in the plan's verification backlog.
