# Issue #855 — Purchases P2: server, credit ledger and RevenueCat webhook

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/855
**Branch:** issue/855-purchases-p2
**Status:** In Progress

## Plan

Spine keeps its own ledger rather than RevenueCat's virtual currency, so it works with any store. The rules sit on the app's enums, which the app and the server share.

The two apps that drive it:

- **Hockeymatch:** one free live match per user, matches bought one at a time, and a subscription that makes every match free.
- **Glukos:** the server needs to know whether a caller's subscription is active, to pick the Live Activity interval.

## Changes

- **Common:**
  - `[Adds]`, `[FreeCredits]` and `[UnlimitedWith]`;
  - `PurchaseCatalog` reads a credit enum (`Create<TPurchase, TProduct, TCredit>`) and checks that `[Adds]` sits only on a consumable, and that every consumable has it;
  - `LedgerEntry` and `CreditLedger`: balance, unlocked, unlimited, missing grants, spend and the answer;
  - `CallerKeys`, the wire contracts and `PurchasesWireJson`;
  - `ProductKind.Consumable` is supported.
- **New `Plugin.Maui.Spine.Server.Purchases`:**
  - `AddSpinePurchases<TPurchase, TProduct[, TCredit]>` with an in-memory or Azure Tables store (a partition per caller, a row per entry, written once);
  - `IPurchaseLedger` (credits, spend, is unlocked, is active, grant);
  - `MapSpinePurchases()` with `GET credits`, `POST credits/spend` and `POST revenuecat`;
  - the RevenueCat webhook: consumables become credits once per transaction, and subscriptions and one-time products become owned products through purchase, renewal, cancellation, refund, expiration and billing issue.
- **`Plugin.Maui.Spine.Purchases`:**
  - `UseSpinePurchases<TPurchase, TProduct, TCredit>` and `o.Credits(c => c.Server = …)`;
  - `ICredits`: balance, unlocked, unlimited, `SpendAsync`, and `UnlockAsync` with the paywall;
  - the server ledger (`ServerCredits`), or the device ledger on the test store (`LocalCredits`);
  - consumables on the paywall ("+3 Live Match") and a paywall for a credit (`ForCredit`).
- **`Plugin.Maui.Spine.Purchases.RevenueCat`:** the default app user id is the caller key (`user:…` / `installation:…`), and consumables are not reported as owned products.
- **Showcase:**
  - the enums moved to `MauiSpineSampleApp.Shared` with a `LiveMatch` credit and two match products;
  - a "Credits" example;
  - the Showcase server maps `MapSpinePurchases("/purchases")`, used when the app runs on RevenueCat.
- **Tests:** 12 server endpoint tests on TestServer, and 6 new catalog and ledger tests in `Plugin.Maui.Spine.Purchases.Tests`.
- **Docs:** the Credits and The server sections in `docs/wiki/purchases.md`, the package README, `packages.md`, and the proposal's status.

## Decisions

- **Rows written once by id instead of a balance counter.** A webhook sent twice, a spend sent twice and a free grant asked for twice all count once, and the ledger doubles as history. The balance is a sum over the caller's partition, which is small.
- **Spends are serialised per caller in one process.** Two instances could both take the last credit for two different references at the same moment. It is accepted, and documented.
- **The caller key is the RevenueCat app user id.** Without that, the webhook could not tell whose ledger a purchase belongs to. An existing anonymous RevenueCat user is aliased on the next `logIn`.
- **The free credit follows the caller key.** An installation that signs in later is a new caller. DeviceCheck to tie it to an iPhone is left for later.
- **Credit rules are attributes on the credit enum**, not options. The app and the server read the same project, as they do for `[Grants]`.

## Tested

- `Plugin.Maui.Spine.Purchases.Tests`: 19 tests pass. `Plugin.Maui.Spine.Server.Purchases.Tests`: 12 tests pass.
- The Showcase on the iOS simulator, on the test store:
  - the free match unlocked match 1;
  - match 2 opened the credit paywall;
  - buying three matches unlocked match 2 and left 2.
- The server tests cover the webhook and the unlimited subscription. The app against the Showcase server through RevenueCat is not tested, because it needs a RevenueCat project.
