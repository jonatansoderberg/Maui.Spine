# Issue #520 — Spine.Server.Authentication: ID-token validation, own tokens and refresh rotation

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/520
**Branch:** issue/520-spineserverauthentication-id-token-validation-own
**Status:** In Progress

## Plan

Step 5.1 of `docs/plans/ai-and-security.md`. The proposal is `docs/proposals/spine-authentication.md` §5.5 and §5.6. The owner's answers that apply here:
- **Q9:** the default user store is a minimal store on Azure Tables, plus `IUserResolver`. There is no ASP.NET Core Identity default.
- **Q7:** sign-in must also work without a Spine server. That is the client's concern (5.2); it does not change this package.

**A new package, `Plugin.Maui.Spine.Server.Authentication`** (`net10.0`, framework reference to ASP.NET Core). It references `.Common`, `Microsoft.AspNetCore.Authentication.JwtBearer` and `Azure.Data.Tables`, and not `Plugin.Maui.Spine.Server`.

- **Registration.** `AddSpineAuthentication(a => …)` with `Issuer`, a signing key, the token lifetimes, the providers, and a store (`UseInMemoryStore()` / `UseAzureTableStore(...)` / `UseUsers<TResolver>()`). It also registers a JwtBearer scheme for Spine's access tokens as the default, so the app's own endpoints use `[Authorize]`.
- **Endpoints**, through `MapSpineAuthentication("/auth")` and static handlers for Azure Functions, as in Server.AI:
  - `GET /auth/nonce`: a one-time nonce, kept server-side.
  - `POST /auth/{provider}/token`: a native ID token plus the raw nonce, exchanged for Spine tokens.
  - `POST /auth/token`: refresh with rotation and family revocation (and later `authorization_code` for the broker).
  - `POST /auth/revoke`: signs out by revoking the family.
  - `DELETE /auth/account`: deletes through the store, revokes Apple's tokens and every family.
- **ID-token validation.** Generic OIDC validation over JWKS from discovery: issuer, audience in the configured client ids, expiry, and the nonce (Apple hashes it with SHA-256). Apple is configured with bundle ids and keeps its authorization-code exchange for the refresh token used in revocation (TN3194). Google and Microsoft ID tokens are the same validation with other issuers.
- **Spine tokens.** The access token is a JWT (`sub`, `iss`, `aud`, `exp`, `provider`), signed with ES256 from a PEM in configuration, and the JWKS is published at `/auth/.well-known/jwks.json`. Refresh tokens are opaque, stored as SHA-256 hashes, and rotated on every use. Presenting a rotated token again revokes the whole family.
- **Users.** A user is linked by (provider, subject). Linking by verified email happens only when `LinkByVerifiedEmail` is set.
- **The push link** in `Plugin.Maui.Spine.Server`: `SpinePushNotificationsOptions.UserId = request => …`. When set, it replaces the body's `UserId` and `user:` tags.
- **Tests** with a stub JWKS that signs its own Apple- and Google-shaped tokens, covering: nonce, audience, expiry, rotation, reuse revoking the family, revocation, account deletion, and the Azure Tables store against Azurite.
- **Docs:** `docs/wiki/authentication-server.md`, the `packages.md` row, a README, the plan, and the files every package PR touches.

## Open Questions

None. The owner answered both on 2026-10-15: split the broker out (#552), and include email and password in this PR.

## Changes

- **`.Common`:** `Authentication/SpineAuthContracts.cs`, the wire records (`SpineUser`, `SpineTokens`, `SpineNonce`, `SpineIdTokenRequest`, `SpineRefreshRequest`, the password bodies, `SpineAuthProblem`), `SpineAuthErrors`, and a source-generated `SpineAuthJson`.
- **New package `Plugin.Maui.Spine.Server.Authentication`:**
  - `SpineAuthOptions` with `Apple`, `Google`, `Microsoft`, `OpenIdConnect`, `EmailPassword` and the stores, and `Validate()`.
  - `Stores.cs`: `IUserResolver`, `ISpineAuthStore` and the in-memory stores. `AzureTableStores.cs`: the Azure Tables stores.
  - `SpineTokenService`: ES256 access tokens, the JWKS, refresh rotation with family revocation.
  - `IdTokens.cs`: `IdTokenValidator` (discovery, nonce, Microsoft's `tid` issuer) and `AppleTokenClient` (code exchange, revocation, the client-secret JWT).
  - `SpineAccounts`: ID-token sign-in, linking, the email and password steps, deletion.
  - `SpineAuthEndpoints` and `MapSpineAuthentication`, plus `GetSpineUserIdAsync()`.
  - `AddSpineAuthentication`, with the JwtBearer default scheme and a startup check.
  - The README and the icon `server-auth.png`.
- **`Plugin.Maui.Spine.Server`:** `SpinePushNotificationsOptions.UserId`. When set, the registration's `UserId` and `user:` tags come from the request.
- **Tests:**
  - `Plugin.Maui.Spine.Server.Authentication.Tests` (60 facts): ID tokens on fake issuers, refresh, revocation, deletion, the password steps, setup, and the Azure Tables stores against Azurite.
  - Three push endpoint facts for `UserId`.
- **Push sample server:** Apple for the Showcase's bundle id, email and password with `LogEmailSender`, `GET /sample/me`, `o.UserId = r => r.GetSpineUserIdAsync()`, and the steps in `send.http`.
- **Docs:**
  - `docs/wiki/authentication-server.md`, plus the README and `packages.md` (count, tables, graph, frameworks) and the spine-setup skill.
  - The proposal's status and decision 7, and `assets/logo-src/README.md`.
  - The plan: 4.2 Done; 5.1 split into 5.1a and 5.1b; Decisions; the verification backlog; Plan history.
- **Build files:** `Spine.slnx`, `Spine.Packages.slnf` and `Directory.Packages.props` (JwtBearer 10.0.8).

## Decisions

- See the plan's Decisions for 2026-10-15 (#520): ES256 with a JWKS, records in `.Common`, hashed refresh families, conditional updates for one-time values, the linking rule, no enumeration, Microsoft without a nonce, and the generic push `UserId`.
- **Azurite caught a real race.** `DeleteEntityAsync` with an ETag reports success when the row is already gone, so five racing callers could all take one nonce. `TakeOneTimeAsync` now marks the row taken with a conditional update and deletes it afterwards. The same test showed `PruneAsync`'s LINQ filter on `GetDateTimeOffset` is unsupported; it uses `TableClient.CreateQueryFilter`. No other package uses a conditional delete.
- **Review before the PR.** An agent reviewed the package. Every finding was checked against the code and fixed with a test:
  - unvouched emails are not stored (pre-account takeover);
  - conditional user updates through `SpineAuthUser.Version` (lockout bypass, a stale sign-in undoing a reset);
  - lockout answers `invalid_credentials`, and emails are sent without waiting (enumeration);
  - one code per purpose and email, used up by any attempt;
  - a 403 owner check on push registrations;
  - limits on input size;
  - Apple exchange failures are caught;
  - the Apple key is created once.
  Not changed: an extra store write on a wrong password for a known email (a small timing difference), and access tokens outliving a deleted account by up to 15 minutes. The wiki says both.
- **Mutation check.** With the nonce comparison removed, `Apple_needs_the_hashed_nonce_in_the_token` failed. The check was then put back.
- **The sample** makes a signing key at startup when `Auth:SigningKey` is missing, so sessions end with a restart, as the in-memory users do.
- **Verified locally** (CI is paused):
  - `dotnet test`: Authentication 60 passed with Azurite in Docker, Server 252 passed, Server.AI 76, Spine.AI 40.
  - Live on the push sample server:
    - Sign-up answered 202, and the code was in the log. Sign-in before verifying answered `email_not_verified`; verifying gave tokens.
    - `/sample/me` answered with the token and 401 without it.
    - A push registration whose body claimed `user:mallory` was stored as `user:<id>` from the token.
    - Refresh worked, and a replay answered `invalid_grant` with the family revoked in the log.
    - Server.AI's report showed the caller as `user:<id>`.
    - Deletion answered 204, and signing in afterwards answered `invalid_credentials`.
    - The nonce and the JWKS answered.
  - Not verified: real Apple, Google and Microsoft tokens, Apple's real endpoints, and Azure Functions (all in the plan's verification backlog).
