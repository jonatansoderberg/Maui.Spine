# Issue #522 — Passkeys: the server and Apple (step 5.4a)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/522
**Branch:** issue/522-passkeys
**Status:** In Review (PR #608)

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

- **Server** (`Plugin.Maui.Spine.Server.Authentication`): `Passkeys(p => …)` with `SpinePasskeyOptions` (relying party, origins, Apple app ids, Android apps, sign-up, challenge lifetime); `SpinePasskeys` over Fido2NetLib 4.2.0; `POST /auth/passkey/{register/options,register,signin/options,signin}` (`SpineAuthEndpoints.PasskeyAsync`); `MapSpinePasskeyAssociations()` for `apple-app-site-association` and `assetlinks.json`; `PasskeyPublicKey` and `PasskeySignCount` on `SpineAuthLogin`, also in Azure Tables; `SpineAuthDefaults.PasskeyProvider`.
- **Contracts** (`.Common`): `SpinePasskeyChallenge`, `SpinePasskeyNewAccount`, `SpinePasskeyResponse`, and the error `unknown_passkey`.
- **App core** (`Plugin.Maui.Spine.Authentication`): the internal `IPasskeyCeremony` and `AddPasskeyCeremony`; the flows (`PasskeySignInAsync`, `PasskeyRegisterAsync`) and the client calls; `ISpineAuth.SignUpWithPasskeyAsync` and `SignInWithPasskeyAutoFillAsync`; `SignInAsync("passkey")` and `LinkAsync("passkey")`; `passkey` in `Providers`.
- **The sheet:** a passkey `SignInButton` ("Sign in with a passkey"), the step `PasskeySignUp` behind "Create account with a passkey", AutoFill while the email field shows (its text content type is `Username` on iOS), English and Swedish text, and `signin-passkey.svg`.
- **New package** `Plugin.Maui.Spine.Authentication.Passkeys`: `a.AddPasskeys()`, `ApplePasskeys` over `ASAuthorizationPlatformPublicKeyCredentialProvider`, and `SpinePasskeyDomain` → the Associated Domains entitlement.
- **Showcase:** `-p:SpinePasskeyDomain=…` turns passkeys on; "Add a passkey" on the Sign-in page; the unlink question says what removing passkeys means.
- **Push sample server:** `Auth:Passkeys:RelyingPartyId` (the Azure host in `appsettings.Production.json`), the Apple app id from the team id it already has, and `MapSpinePasskeyAssociations()`. Deployed to Azure.
- **Tests:** 18 server tests with a software authenticator (ES256, "none" attestation), 5 client flow tests against a real server.
- **Docs:** the wikis, the package README, the README and packages tables, the spine-setup skill, and the plan (5.4a/b/c, Q12).

## Decisions

- 2026-10-17, owner: the Azure domain; Fido2NetLib; Apple first, then Android (#601) and Windows (#602); all four ways into the app.
- **A passkey is a login with its key on `SpineAuthLogin`,** not new `IUserResolver` methods as the plan said: the stores keep it with the login, and nothing else changes for an app's own resolver. AAGUID and transports are not kept; nothing reads them yet.
- **`unknown_passkey`** is its own error: "Wrong email or password" would be wrong for a passkey that was removed from the account. A refused signature or user handle is `invalid_token`.
- **AutoFill is iOS only** (Mac Catalyst's binding has no `PerformAutoFillAssistedRequests`). A button's request cancels a pending AutoFill request first, and AutoFill waits for the sheet to close.
- **`excludeCredentials`** goes through `IASAuthorizationWebBrowserPlatformPublicKeyCredentialRegistrationRequest`, where the .NET binding has it, when the request responds to `setExcludedCredentials:` (iOS 17.4).
- **The simulator needs a signed build** for passkeys: without `application-identifier` in the entitlements iOS answers 1004 ("process without application identifier"). An incremental build after adding `CodesignKey` kept the old embedded entitlements; a clean simulator build was needed.
- **After the review:** the passkey button follows the step like the others (it showed in apps without passkeys); AutoFill steps aside while Apple's or a provider's sheet is up, is made again every four minutes before the challenge expires, and retries quietly every 30 s when the challenge cannot be fetched, showing only a refusal of a passkey the user picked; a credential id over 255 bytes is refused before anything is written, and a new account whose passkey cannot be saved is removed; a counter is saved only while the passkey is still there; Spine's own sheet is skipped as before when passkeys are not supported on the device.
- **One passkey at a time:** `SpineLoginInfo.Subject`, `DELETE /auth/link/{provider}/{subject}` and `ISpineAuth.UnlinkAsync(provider, subject)`; the Showcase's Unlink on a passkey row removes that passkey only.
- **Left as they are, for now:** adding a passkey needs only a valid access token, like linking any other provider; the unauthenticated options endpoints write a one-time row each, like `/auth/nonce`, and the sample server rate-limits `/auth`.
- **Symbol:** `Passkey` in the icon set (233 icons): the set's User and an upright Key, top and bottom aligned, the key's hole lowered a little; `signin-passkey.svg` is the same glyph.

## Verification

- The owner's iPhone against Azure (2026-10-17): a new account with a passkey, sign-in with the button, and sign-in through AutoFill.
- The iPhone 17 simulator against Azure: sign-up and sign-in with the button.
- Server tests 127, app tests 74.
