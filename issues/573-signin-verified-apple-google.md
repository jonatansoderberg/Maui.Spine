# Issue #573 — Sign-in verified against Apple and Google in the Showcase; IdTokens doc warnings

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/573
**Branch:** issue/573-signin-verified-apple-google
**Status:** In Review

## Plan

Record what ran against the real providers on 2026-10-16, and fix the three CS1573 warnings #564 left in `IdTokens.cs`.

## Changes

- `IdTokens.cs`: every parameter of `ValidatedIdToken` documented (CS1573).
- Plan:
  - 5.3b and its package row set to Done (#570);
  - the 5.3a backlog row now says what ran and what is left (Credential Manager on Android), with the Google Cloud project's setup;
  - Last updated and Plan history.
- Wiki: the "not verified" note for Google.

## Verification

All in the iPhone 17 simulator, against the push sample server:

- **Apple:** the real sheet with the owner's Apple ID. Signed in with the names and the email.
- **Google:** the broker with a new Google Cloud project, "Spine Showcase".
  - The consent screen is in Testing.
  - A Web client and an Android client.
  - The owner signed in. The code was traded at Google, and the app showed the picture, the names and the verified email.
- **Microsoft:** both modes (#570).
- All three show together in the sign-in sheet.
- **The server builds without the CS1573 warnings.**
