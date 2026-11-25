# Issue #873 — Purchases: an event after a paywall closes with a purchase

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/873
**Branch:** issue/873-paywall-purchased (on issue/872-title-icon)
**Status:** In Progress

## Plan
`IAppPurchases.Purchased`, raised by `AppPurchases.OpenPaywallAsync`, the one path every paywall takes, once it has returned with a purchase or a restore.

## Changes
- `IAppPurchases.Purchased` and `PaywallPurchasedEventArgs` (`Purchase`: what the paywall was opened for; `Active`: the purchases active afterwards).
- `docs/wiki/purchases.md`: "After a purchase".

## Decisions
- Raised after the navigation returns, so the paywall sheet is closed and the app can open a sheet of its own. `Changed` comes earlier, while the paywall is still open, which is why it does not do for a welcome.
- Not raised for `PurchaseAsync` called directly: the app that called it already knows.
