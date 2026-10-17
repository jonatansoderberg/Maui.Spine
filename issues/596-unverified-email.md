# Issue #596 — Spine.Authentication: pass an unverified email on, with SpineUser.EmailVerified

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/596
**Branch:** issue/596-unverified-email
**Status:** In review

## Plan

The owner decided on 2026-10-17, answering the question left open in #564: pass a personal Microsoft account's email on, marked unverified.

## Open Questions

## Changes

- **Master merged in (2026-10-17)** after passkeys (#522, #611, #616): a passkey sign-up's email now reaches the app as given, with `EmailVerified` false, so the passkey test checks that instead of no email.
- **`SpineUser.EmailVerified`:** an optional `bool`, false by default, after `Logins`, so existing callers compile.
- **Server:**
  - `SpineAuthUser.ToContract` gives the account's own email with its flag. Without one, it gives the email the provider of this sign-in gave, with `EmailVerified` false.
  - The account's stored email is unchanged: verified-only, because password resets and linking by email trust it.
- **`UseIdentityProvider`:** reads `email_verified`, a boolean or the string "true".
- **`UseMicrosoft`:** passes MSAL's sign-in name as the email only when it is an address (a phone number is not), unverified.
- **Showcase:** the Sign-in page marks an unverified email.
- **Tests:**
  - three server tests now assert the unverified email in the contract and no email on the account;
  - a client test covers `email_verified` false.
- **Docs:** the authentication wiki's table of the user's details, and the plan.

## Decisions

- OIDC's shape, `Email` plus `EmailVerified`, rather than a second property such as `ContactEmail`. **Why:** it is what an app that knows OpenID Connect expects, and a provider's email that another account owns (Google vouches for it, but it is not this account's) also fits as "shown, not trusted".
