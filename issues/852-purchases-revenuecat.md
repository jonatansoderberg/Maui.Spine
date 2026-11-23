# Issue #852 — Purchases P1: the RevenueCat adapter

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/852
**Branch:** issue/852-purchases-revenuecat (on issue/849-purchases-p1, PR #851)
**Status:** In Progress

## Plan

An `IPurchaseStore` over Plugin.RevenueCat 0.4.44 in a new package, `Plugin.Maui.Spine.Purchases.RevenueCat`, picked with `o.UseRevenueCat(…)` inside `UseSpinePurchases`. The Showcase uses it when its build gets the RevenueCat keys, and the test store otherwise. Real purchases need a RevenueCat project and store products, which are outside this issue.

## Changes

- `Plugin.Maui.Spine.Purchases.RevenueCat`:
  - `UseRevenueCat(r => …)` with the Apple and Google SDK keys, `Debug` and `AppUserId`;
  - `RevenueCatStore` maps customer info to `OwnedProduct` (grace period, billing retry, trial, intro, refunds, one-time products);
  - products and prices by id, with the free trial the user is eligible for;
  - purchases, with Play base plans through their subscription option and a change of plan with proration;
  - restore, and the management URL;
  - the app user id follows `ISpineSignedInUser` or the app's own id;
  - `[Spine]` warnings for entitlements that disagree with `[Grants]`, products missing in the store, and owned products no enum member names.
- `Plugin.Maui.Spine.Purchases`:
  - `SpinePurchasesOptions.Builder` for adapters;
  - `RefreshAsync` keeps the last known state on a store error;
  - the cached state is kept per store;
  - the paywall opens at once and asks the store after, with a spinner while nothing is known;
  - the primary button is hidden when nothing can be bought.
- Showcase: RevenueCat when built with `-p:SpineRevenueCatAppleKey=…` / `-p:SpineRevenueCatGoogleKey=…`, the test store otherwise. The test store's controls show only on the test store.
- Docs: the RevenueCat section in `docs/wiki/purchases.md`, the package README, and a row in `docs/wiki/packages.md`.

## Decisions

- **Products by id, not offerings.** The app's enum already says what is sold and in which order (`Offer`). Offerings would be a second list to keep in step in RevenueCat's dashboard.
- **On Play, a subscription is bought by its subscription option.** Plugin.RevenueCat's purchase by product id buys the first base plan Play returns. The option is the default one for the base plan, which includes the trial offer the user is eligible for.
- **The adapter calls `Initialize()` itself** when the SDK has no key yet. Spine's first refresh runs from an `IMauiInitializeService`, before Plugin.RevenueCat's `FinishedLaunching` hook. Plugin.RevenueCat ignores a second initialization.
- **MAUI 10.0.110 for this package only** (`VersionOverride`). Plugin.RevenueCat 0.4.44 needs it. The other Spine packages stay on 10.0.50.
- **The paywall no longer waits for the store.** Spine shows a sheet only after `OnAppearingAsync` returns. With a fake RevenueCat key the sheet took seconds to come up, so the load now runs after the sheet is shown.
- **The state is cached per store.** After a switch from the test store to RevenueCat, the test store's Supporter purchase was still shown as active.

## Tested

- The adapter builds for iOS, Android and Mac Catalyst.
- The Showcase runs on the iOS simulator:
  - without keys it uses the test store, as before;
  - with a fake Apple key it starts, and the paywall opens at once and says nothing is for sale.
- Not tested: real purchases. They need a RevenueCat project and sandbox products.
