# Issue #522 — Passkeys: the server and Apple (step 5.4a)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/522
**Branch:** issue/522-passkeys
**Status:** In Progress

## Plan

The owner decided on 2026-10-17:
- **The relying party** is the Azure sample server, `spine-showcase-e41ddd.azurewebsites.net` (#581). It serves the association files itself.
- **WebAuthn on the server** is Fido2NetLib (`Fido2` 4.2.0, more than two weeks old).
- **Apple first:** 5.4a is the server plus iOS and Mac Catalyst (this issue), 5.4b Android (#601), 5.4c Windows (#602).
- **In the app:** all four:
  - "Continue with passkey" in the sheet;
  - "Add a passkey" to a signed-in account;
  - passkeys offered by AutoFill in the email field;
  - a new account made with a passkey.
- **Out:** iOS 26's account-creation provider (passkey-only sign-up from the system), as the proposal said.

### Server (`Plugin.Maui.Spine.Server.Authentication`, behind `a.Passkeys(p => …)`)

- **Options:**
  - `RelyingPartyId` (the domain) and `RelyingPartyName`;
  - `Origins` (default `https://{RelyingPartyId}`);
  - `AppleAppIds` (`TEAMID.bundle`) for `apple-app-site-association`;
  - `AndroidApps` (package and SHA-256 fingerprints) for `assetlinks.json` and the `android:apk-key-hash:` origins, so #601 only fills them in.
- **Endpoints** under the auth prefix, each answering Fido2's options JSON with a one-time challenge id (5 minutes, in the one-time store):
  - `POST /auth/passkey/register/options`: with a Bearer, a passkey for the signed-in user; without one, a new account from `{ displayName, email? }`, when sign-up is allowed.
  - `POST /auth/passkey/register`: the attestation; answers tokens for the new account, or for the user the passkey was added to.
  - `POST /auth/passkey/signin/options`: discoverable credentials, so no username is needed (this is what AutoFill uses as well).
  - `POST /auth/passkey/signin`: the assertion, the signature counter checked and updated; answers tokens.
- **Associations:** `app.MapSpinePasskeyAssociations()` serves `/.well-known/apple-app-site-association` (`webcredentials`) and `/.well-known/assetlinks.json` (`get_login_creds`) at the site's root.
- **Storage:**
  - Each passkey is a login with provider `passkey` and the credential id as subject, so `Logins`, `SignInMethods`, unlinking and "never the last way in" work as they do for the others.
  - Its public key, counter, AAGUID and transports are kept beside it: `IUserResolver` gains passkey methods as default interface members, and the in-memory and Azure Tables stores implement them.
- **A new account made with a passkey:** the email it gives is kept on the login, unverified (#596), not as the account's email. A code is the way to verify an email, as today.
- **Tests:** register and sign in with Fido2's test authenticator (a software authenticator), a challenge used twice or expired, an unknown credential, a counter that goes back, adding to a signed-in user, the last way in, and the association files.

### App (`Plugin.Maui.Spine.Authentication.Passkeys`, new)

- **Setup:** `a.AddPasskeys()`, and the build property `SpinePasskeyDomain` adds `com.apple.developer.associated-domains` = `webcredentials:{domain}` through `SpineEntitlement`.
- **iOS 16+ and Mac Catalyst:** `ASAuthorizationPlatformPublicKeyCredentialProvider` for registration and assertion, in the core's provider hook, which grows a passkey ceremony (options JSON in, the credential out).
- **AutoFill:** `PerformAutoFillAssistedRequests` while the email field has focus (`textContentType` username), which signs in when the user picks a passkey.
- **The sheet:**
  - a `SignInButton` with provider `passkey` ("Continue with passkey", a new `Passkey` symbol through /spine-symbol);
  - "Create account with a passkey" on the sign-up step (name and email);
  - the email field offering passkeys;
  - English and Swedish.
- **`ISpineAuth`:** `LinkAsync("passkey")` adds one. `UnlinkAsync("passkey")` removes them (asked first on the Showcase page, as today).
- **Showcase:** passkey on the Sign-in page and in the sheet, built with `-p:SpinePasskeyDomain=spine-showcase-e41ddd.azurewebsites.net`.

### Apple portal

Associated Domains on the App ID `se.cosmomedia.spineshowcase`, then `Spine Showcase Development` regenerated, in the browser pane with the owner signed in, as was done for Sign in with Apple.

### Verification

- Server tests with the software authenticator.
- The Azure server serves the association file, and Apple's CDN (`app-site-association.cdn-apple.com`) has it.
- On the owner's iPhone against Azure:
  - a new account with a passkey;
  - sign-out, then sign-in with the button and through AutoFill;
  - "Add a passkey" to a Google account;
  - unlink.

### Docs

The wikis (`authentication.md`, `authentication-server.md`), the new package's README, the plan (5.4a/b/c, Q12 answered), the proposal's status, and this changelog.

## Open Questions

## Changes

## Decisions

- 2026-10-17, owner: the Azure domain; Fido2NetLib; Apple first, then Android (#601) and Windows (#602); all four ways into the app.
