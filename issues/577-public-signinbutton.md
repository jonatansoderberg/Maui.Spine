# Issue #577 — Spine.Authentication: a public SignInButton with each provider's logo and colours

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/577
**Branch:** issue/577-public-signinbutton
**Status:** Completed

## Plan

The sign-in sheet's provider buttons (#575) were private to the sheet. Make the button a public control, `SignInButton`, in `Plugin.Maui.Spine.Authentication`. The sheet uses it, and so do the Showcase's "Link …" buttons. The owner also asked for an `Unlink` symbol (2026-10-16), for the Showcase's unlink buttons.

## Open Questions

## Changes

- `SignInButton` (`ContentView`): `Provider` picks the logo and the light and dark colours (`apple`, `google`, `microsoft`, `password`, or the neutral outline). `Text` defaults to "Continue with …" or "Continue with email". `Command`, `CommandParameter` and `Clicked` are as on a `Button`. The logo is hidden from screen readers; the button's text names the provider. `ProviderLook` moved with it from the sheet.
- The sign-in sheet builds its provider, email and "I've signed in before" buttons with `SignInButton`; it looks as before.
- New symbol `Unlink` (option a: the `Link` glyph's two links without the bar, with short sparks on each side of the break); the set has 232.
- Showcase: the Sign-in page lists each login with its logo (`signin-*.svg` from the package) and an `Unlink` icon button, and the "Link …" buttons are `SignInButton`s. `GlobalXmlns.cs` maps `Plugin.Maui.Spine.Authentication` into the global XAML namespace.
- Wiki: `SignInButton` in `docs/wiki/authentication.md`.

## Verification

In the iPhone 17 simulator against the push sample server: a password account's row shows the password symbol and the Unlink icon; the Link Apple, Google and Microsoft buttons show their logos and colours; the Unlink icon on the last login answers `last_login`; the sign-in sheet's buttons look as before. Built for iOS.

## Decisions

- 2026-10-16, owner: a public control in the package rather than styling the Showcase's buttons on their own, so an app's own sign-in page gets the same buttons. `Unlink`: option a.
