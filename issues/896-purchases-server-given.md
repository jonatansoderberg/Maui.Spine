# Issue #896 — Purchases section: show a purchase the server gave

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/896
**Branch:** issue/896-purchases-server-given
**Status:** Completed

## Plan
The server's credits answer already says which credits are unlimited; the Purchases section only asked the store. Let
the section trust the server where no store purchase explains an unlimited credit.

## Changes
- `AppCredits.IsUnlimitedByServer(credit)` (internal): the server's answer alone.
- `PurchasesSection`: a credit the server makes unlimited without a store purchase greys its row as Unlimited; a single
  purchase that makes it unlimited shows as owned with "Given" / "Gåva"; none of its purchases is offered.
- Strings `Spine.Purchases.Row.Given` (en, sv); `docs/wiki/purchases.md`.

## Decisions
- The answer names credits, not purchases. Where several purchases make one credit unlimited (a season card and a
  subscription), the section cannot tell which was given, so it names none and only stops offering them.
