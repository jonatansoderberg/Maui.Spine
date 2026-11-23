# Issue #849 — Purchases P1: IAppPurchases, paywall, guard and test store

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/849
**Branch:** issue/849-purchases-p1
**Status:** In Progress

## Plan

Build the client side of phase P1 in `docs/proposals/spine-purchases.md`, without a real store adapter, so the Showcase can be tried on the test store:

1. `Plugin.Maui.Spine.Common/Purchases`: the attributes, `ProductKind`, `PurchasePhase`, `PurchaseState`, `OwnedProduct`, and `PurchaseCatalog` / `PurchaseResolver` (shared with the server in P2).
2. `Plugin.Maui.Spine.Purchases`: `IAppPurchases`, `IPurchaseStore`, `UseSpinePurchases<TPurchase, TProduct>`, the guard, `Purchase.Command` / `Purchase.Requires`, `{PurchaseActive}`, the paywall sheet, `PurchasesSection`, the test store.
3. The Showcase's Purchases page, unit tests for the catalog and resolver, `docs/wiki/purchases.md`, the Purchases section in `settings-pages.md`.

Next issues: the RevenueCat adapter (P1, needs a RevenueCat account and store products), then the server package (P2).

## Changes

- `Plugin.Maui.Spine.Common/Purchases`: `[PurchaseId]`, `[StoreProduct]`, `[Grants]`, `ProductKind`, `PurchasePhase`, `PurchaseState`, `OwnedProduct`, `StorePlatform`, `PurchaseCatalog`, `PurchaseResolver`.
- New package `Plugin.Maui.Spine.Purchases`:
  - `AppPurchases`: the last known owned products in `Preferences`, `Changed` on the main thread, and an overlap reported once;
  - `[RequiresPurchase]` / `PurchaseGuard` (order 150, with a cover);
  - `Purchase.Command` / `Purchase.Requires` (a mark on a `SpineRow`) and `{PurchaseActive}`;
  - `PaywallSheet` with per-purchase text (`.For(purchase, …)`);
  - `PurchasesSection`;
  - `TestStore` / `ITestStore`;
  - strings in English and Swedish.
- Showcase:
  - a Purchases page with Pro (monthly, yearly with a 7-day trial, lifetime) and a Supporter one-time purchase, and the test store's controls;
  - a `StatisticsPage` behind `[RequiresPurchase]`;
  - registered in `MauiProgram` with `AllowTestStoreInRelease`.
- `tests/Plugin.Maui.Spine.Purchases.Tests`: 13 tests for the catalog and the resolver.
- Docs:
  - `docs/wiki/purchases.md` and the package README;
  - the Purchases section (now section 2) and pink for purchases in `docs/wiki/settings-pages.md`;
  - rows in `docs/wiki/packages.md`.

## Decisions

- **Stores report owned products and Spine applies `[Grants]`**, for every adapter. With RevenueCat the adapter still reports products, so the app's enums stay the single source of what a product gives. The start-up check against RevenueCat's entitlements comes with the adapter.
- **The Release check is at start-up, not at build time.** `UseSpinePurchases` reads `DebuggableAttribute` on the calling assembly and refuses the test store in a Release build unless `AllowTestStoreInRelease` is set. A build-time error would need an analyzer or a generated attribute in the app.
- **`Purchase.Requires` on a `SpineRow` only marks the row.** The paywall in front of the tap comes from `Purchase.Command`. Setting `SpineRow.Command` from code drops the row's own binding, as found in the simulator, so a bound `Command` cannot be wrapped.
- **`UseSpinePurchases` is not a `SpineModule`.** It is generic over the app's enums, so the app calls it itself, after `UseSpine()`, which the paywall sheet's registration needs.
- **No shimmer on the paywall yet.** The products come from the last answer kept on the device, and the test store answers at once. The shimmer can come with the RevenueCat adapter, whose first answer takes time.
- **Hockeymatch wants match credits:** one free live match per user, more bought one at a time, or a subscription. That is `ProductKind.Consumable` with a balance on the server (proposal §8). It goes with P2, not here.
