# Issue #616 — Logins: keep when each was last used, and label the dates

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/616
**Branch:** issue/616-login-last-used
**Status:** In Review

## Plan

The owner asked on 2026-10-17: label the dates on every login row, and keep "last used" for every way to sign in, not only passkeys.

## Changes

- `SpineAccounts`: `LastUsedAt` at every sign-in through `ResolveAsync` (ID tokens and the browser broker), a pending identity made into an account, and every password sign-in (`PasswordSignedInAsync`: sign-up without verification, a verified code, a password, a reset).
- Showcase: every login row shows "added …" and "last used …"; a passkey row also where it is kept and its device.
- Test: a password sign-up, a Google link, a password sign-in and a Google sign-in, each moving only its own login's `LastUsedAt`.
- Docs: both wikis, the plan's history.

## Decisions

- A link counts as a use: the user signs in with the provider to add it, and a row that said nothing after "I've signed in before" looked wrong to the owner (first a link left it empty; changed on 2026-10-17).
