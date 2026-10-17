# Issue #611 — Passkeys: show where each is kept, the device it was made on, and when it was last used

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/611
**Branch:** issue/611-passkey-details
**Status:** In Progress

## Plan

The owner agreed on 2026-10-17 (follow-up to #522):
- **Server:** at registration keep the authenticator's AAGUID and the device name the app sends (`SpinePasskeyResponse.DeviceName`, optional) on the login; at each passkey sign-in set `LastUsedAt`. `SpineLoginInfo` gains `Authenticator` (a name from `SpinePasskeyOptions.AuthenticatorNames`, a table of well-known AAGUIDs the app can extend), `DeviceName` and `LastUsedAt`. Azure Tables keeps the new columns.
- **App:** the passkey registration sends `DeviceInfo.Name` (iOS 16 and later answer the model, "iPhone", without the user-assigned-device-name entitlement).
- **Showcase:** a passkey row reads "iCloud Keychain · iPhone · last used …".
- **Tests:** server and client.

## Changes

- Server: `SpineAuthLogin.PasskeyAaguid`, `PasskeyAuthenticator`, `DeviceName`, `LastUsedAt` (also in Azure Tables); `SpinePasskeyOptions.AuthenticatorNames`; registration keeps them, each passkey sign-in sets `LastUsedAt`.
- Contracts: `SpineLoginInfo.Authenticator`, `DeviceName`, `LastUsedAt`; `SpinePasskeyResponse.DeviceName`.
- App: the registration sends `DeviceInfo.Name`.
- Showcase: a passkey row reads "iCloud Keychain · iPhone · last used …".
- Tests: two server tests (named authenticator, device, last used moving on; an unknown AAGUID), the client flow sends the device name.
- Docs: both wikis, the plan (5.4a Done, #611).

## Decisions

- The authenticator's name is resolved at registration and kept with the login, so the contract mapping needs no options.
- The device name is the app's label, not checked; on iOS 16+ it is the model ("iPhone") without the user-assigned-device-name entitlement, which the Showcase does not ask for.
