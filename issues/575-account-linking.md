# Issue #575 — Spine.Authentication: account linking (one account, several providers, onboarding that asks before a new account)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/575
**Branch:** issue/575-account-linking
**Status:** In review

## Plan

A new step, 5.5, in `docs/plans/ai-and-security.md`. The owner chose on 2026-10-16:
- a **pending identity before any account is made**, rather than merging afterwards;
- **refuse a conflict** with an explanation;
- run it **here, as its own step**.

**What exists:** the server keeps several logins per user (`SpineAuthLogin`), and the app always sees the server's `Id`. `SignInMethods` (#564) lists the providers. Today a link happens only through `LinkByVerifiedEmail`.

### Server (`Plugin.Maui.Spine.Server.Authentication`)

- **Per login:** `SpineAuthLogin` gains `Email`, `DisplayName` and `LinkedAt` (in memory, and as Azure Tables columns).
- **The user's logins:** `SpineUser.Logins` lists them (`SpineLoginInfo`: provider, email, name, linked at). This is the provider detail the app can show.
- **Linking while signed in** (`Authorization: Bearer`, the user's access token, is the proof):
  - `POST /auth/link/{provider}` with an ID token (Apple's, Google's or Microsoft's sheet) attaches the identity to the signed-in user.
  - `POST /auth/link/ticket` gives a one-time ticket for the browser (5 minutes). `GET /auth/{provider}/start?…&link=<ticket>` carries it through the broker, so the callback links instead of signing in.
  - The answer is new tokens, with `Logins` and `SignInMethods` updated.
- **A conflict:** an identity that belongs to **another** user gets 409 `identity_in_use`, with a detail that says what to do. An identity the user already has is a no-op.
- **Unlinking:** `DELETE /auth/link/{provider}` (Bearer). The last way to sign in, including a password, is never removed: 409 `last_login`.
  - `IUserResolver` gains `DeleteLoginAsync`.
  - It is a default interface method that throws `NotSupportedException`, so an app's own resolver still compiles. The built-in stores implement it.
- **Pending identity:** `a.AskBeforeNewAccount = true`, off by default.
  - A sign-in with an unknown identity (`/{provider}/token`, or the broker's code exchange) creates no user. It answers **202** with a `SpinePendingIdentity`: a ticket, the provider, an email if one is vouched for, a name, and an expiry of 10 minutes. The identity's details are kept with the ticket in the one-time store.
  - `POST /auth/pending/create {ticket}` creates the user and the login, and answers with tokens.
  - `POST /auth/pending/link {ticket}` with a Bearer of the account the user proved (with another provider or a password) attaches the pending login to that account and answers with tokens. A conflict is refused as above.
  - `LinkByVerifiedEmail` keeps working, before the question: a known, verified email links directly.
- **Contracts in `.Common`:** `SpinePendingIdentity`, `SpineLinkTicket`, `SpinePendingRequest` and `SpineLoginInfo`. `SpineAuthErrors` gains `IdentityInUse` and `LastLogin`.

### Client (`Plugin.Maui.Spine.Authentication`)

- **New `ISpineAuth` members:**
  - `LinkAsync(provider)`: the provider's sheet or the broker (with a link ticket), then new tokens.
  - `UnlinkAsync(provider)`.
  - `CreateAccountAsync(pending)`.
  - `LinkPendingAsync(pending)`: needs a signed-in user, the account the user just proved.
  - All are default interface members answering `Failed`, so an app's own `ISpineAuth` still compiles.
- `SignInStatus.ChooseAccount` with `SignInResult.Pending` (`SpinePendingIdentity`).
- **The native hook and the browser flow** both use the same server calls with a link or pending intent. Google's Credential Manager and MSAL come along without changes to their packages.
- **The sign-in sheet's new step** after a pending identity: "Welcome! Is this your first time here?"
  - **Create a new account.**
  - **I've signed in before**, which shows the other providers and the password field, and links after the sign-in. If that sign-in is also unknown, the sheet says so and goes back to the choice.
  - Back.
  - Strings in English and Swedish.
- **The mode without a server** (`UseIdentityProvider`/`UseMicrosoft`): linking needs a server, so the new methods answer `Failed` there, as the password methods do.

### Showcase and the push sample server

- **The push sample server:** `AskBeforeNewAccount = true`.
- **The Sign-in page** shows `Logins` (provider, email, linked), a "Link …" button per provider the account lacks, and "Unlink" per login.

### Tests

- **Server:**
  - linking with an ID token and through the broker with a ticket;
  - a conflict, a no-op, unlinking, and the last login refused;
  - a pending identity: created, linked after proof, expired, a ticket used twice, and a conflict;
  - `Logins` after a refresh.
- **Client:** `LinkAsync`/`UnlinkAsync`, `ChooseAccount`, `CreateAccountAsync` and `LinkPendingAsync` against a real `Server.Authentication` in the TestServer.

### Verification

In the iPhone 17 simulator against the push sample server, with Apple, Google and Microsoft (all set up on 2026-10-16):
- the first sign-in with Google gives the choice; "I've signed in before" with Apple ends with one account with two logins;
- Microsoft linked from the page;
- unlinking;
- the same `Id` whichever way the user signs in.

### Docs

The wikis (`authentication.md`, `authentication-server.md`), the plan (5.5, Decisions, backlog), the proposal's status, and this changelog.

## Open Questions

- None. The unverified Microsoft contact email from #564 stays open there.

## Changes

<!-- Updated as work progresses -->

- New symbol `Merge` (two lines joining into one arrow) in `Plugin.Maui.Spine.Svg.Icons`, for merging accounts; the set has 229.
- New symbols `Password` (a password field with dots and a cursor) and `AccountLink` (the existing `User` and `Link` glyphs, scaled, with the link as a badge at the bottom right); the set has 231.
- The sign-in sheet's buttons share one shape (48 high, radius 8, a 20-point logo on the left, the text centred), "Continue with …", in each brand's light and dark colours: Apple black/white, Google white or #131314 with its outline, Microsoft white or #2F2F2F. Email gets a matching "Continue with email" button that opens the fields when there are providers, and the fields show at once without them.
- Server: `POST /auth/link/ticket`, `POST`/`DELETE /auth/link/{provider}` and `POST /auth/pending/{create,link}`, with the caller read from the access token (`sub` and the provider claim); a 202 answer carries the `SpinePendingIdentity`. A password sign-up's login gets its email and `LinkedAt`, and a reset keeps an existing password login.
- Server tests: `AccountLinkingTests` (16: ID token and broker links, a conflict, a no-op, unlinking, the last login, removing the password, a pending identity created, linked, conflicting, expired and through the broker, a verified email linking first, `Logins` after a refresh) and an Azure Tables test for a login's details and `DeleteLoginAsync`. All 113 pass against Azurite.
- Client: `ISpineAuth.LinkAsync`, `UnlinkAsync`, `CreateAccountAsync`, `LinkPendingAsync` (default members), `SignInStatus.ChooseAccount` with `SignInResult.Pending`; the flows take a link token (the sheet's path, the browser with a link ticket); a link raises `UserChanged` only. Client tests: `AccountLinkingTests` (8) against a real server; all 68 pass.
- The sheet: the steps `Choose` ("Is this your first time here?": create, or "I've signed in before") and `Prove` (the other ways; the new one is added after the sign-in), in English and Swedish, with the errors `identity_in_use` and `last_login`.
- Showcase: "One account, several ways in" on the Sign-in page (the logins, Unlink, Link per missing provider); the push sample server sets `AskBeforeNewAccount`.
- Verified in the iPhone 17 simulator against the push sample server: a password account; Apple linked and unlinked from the page; the last login refused; after the unlink, Apple again gave "Welcome", "I've signed in before" hid Apple and showed the rest, the password sign-in added Apple, and the account signs in with apple and password. Google and Microsoft links were not run in the simulator (they need the owner's sign-ins); the tests cover both paths.
- `Plugin.Maui.Spine.Svg` draws two things in Figma exports that Svg.Skia does not: a CSS `conic-gradient` in a `foreignObject` (drawn with Skia's sweep gradient, `SvgCssFeatures`) and `mask-type: alpha` (the mask's colours whitened). Six tests in `SvgCssFeaturesTests`, and the wiki's SVG page.
- The logos are embedded SVGs in the Authentication package (`Resources/Svg/signin-*.svg`, registered with `UseEmbeddedSvgImages`): Apple's drawn for Spine, Google's G from its Sign in with Google assets, Microsoft's four squares to its spec, and the `Password` symbol.

## Review

A review agent read the whole diff before the PR. Fixed:
- **Link CSRF in the broker (security).** A browser link was made at the provider's callback, before PKCE, so a start URL with someone's link ticket, opened by another person, would have attached that person's provider account to the ticket's owner. The callback now only validates the identity (`SpineAccounts.IdentityAsync`), and the link happens at the code exchange (`LinkBrokeredAsync`), which only the device holding the PKCE verifier can make. Two tests: nothing is linked after the callback or with a wrong verifier, and a conflict is a 409 at the exchange.
- **The sheet lost the link** when "I've signed in before" went through an emailed code: it checked the step rather than the choice. It now keeps `_proving` through the code step.
- **A failed link was silent:** the sheet now closes with the sign-in and the reason in `SignInResult.Error`.
- **A link rewrote the account's profile** (a personal account would have cleared a work account's organization). A link leaves the profile alone.
- **SvgCssFeatures:** malformed XML or CSS returns null instead of throwing, a BOM is skipped, `rgb()` channel percentages are of 255, an alpha mask keeps each colour's alpha, and a shape without a fill in an alpha mask shows. Five more tests.

Left, as known limits:
- Linking and the link ticket take any valid access token, with no recent sign-in required. A stolen access token (at most `AccessTokenLifetime`) could add the thief's own provider account. A "recent sign-in" check belongs with passkeys (5.4).
- Two concurrent unlinks of different providers could leave an account with no login; the stores have no transaction across logins.
- An Apple refresh token in a pending identity that is never chosen is not revoked when it expires.
- A pending ticket is used up by a 409, so the user starts that sign-in again.
- The SVG rewrite ignores a `foreignObject`'s own `transform`, `opacity` and `clip-path` (Figma puts them on the parent group), and does not whiten gradients defined outside the mask.

## Decisions
- 2026-10-16: Apple's logo is drawn for Spine, not taken from Apple's Sign in with Apple download. That download's license (Apple Design Resources) allows mock-ups only and forbids embedding the files in software. The logo shows only on Apple platforms; elsewhere Apple goes through the browser with the same button.
- 2026-10-16, owner: vector graphics, not embedded pixels, and support for the gradient in Spine's SVG rather than an approximation. `signin-google.svg` is Google's own file (iOS, light, logo only) without the button square, cropped to the G. Through `SvgRasterizer` it differs from Google's PNG by 0.95/255 per pixel on average. The sweep is drawn at load, 512 pixels across its box, since the size it is shown at is not known yet; a conic gradient has no edges inside it, so this stays smooth when scaled.

- 2026-10-16, owner: a pending identity before a new account (`AskBeforeNewAccount`), a conflict refused with an explanation, its own step done in this session.
