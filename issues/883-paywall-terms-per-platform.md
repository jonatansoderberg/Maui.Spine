# Issue #883 — Paywall: a terms link per platform (Apple's standard EULA on iOS, the app's or Play's terms on Android)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/883
**Branch:** issue/883-paywall-terms-per-platform
**Status:** Completed

## Plan

`PaywallOptions.Terms(Uri)` takes one link for every platform. An app without terms of its own uses Apple's standard EULA on iOS, which does not fit a Play build, so Hockeymatch branches with `#if ANDROID`. Add a per-store overload and a named constant for Apple's EULA; keep `Terms(Uri)` as the link for every store.

## Open Questions

## Changes

- `Plugin.Maui.Spine.Common/Purchases/PaywallTerms.cs`: `PaywallTerms.AppleStandardEula`, and the internal `StoreLinks` (a link for every store plus one per `StorePlatform`, `For(store)`, `Current`).
- `PaywallOptions.Terms(Uri? apple, Uri? google, Uri? microsoft = null)`; `Terms(Uri)` sets the link for every store. `PaywallSettings.Terms` is a `StoreLinks`.
- Start-up: a per-store `Terms` without a link for the running platform's store stops with its own message naming the store; otherwise the existing "needs .Terms(…) and .Privacy(…)" message.
- The paywall opens the running store's link.
- Tests: `PaywallTermsTests` (fallback order, a store without a link, the EULA URL, the current store on a test run).
- Docs: `purchases.md` (Setup), `purchases-stores.md` (The app), the `spine-purchases` skill.

## Decisions

- **`microsoft`, not `windows`:** the parameter names follow the existing `StorePlatform` (Apple, Google, Microsoft) and `[StoreProduct(Apple = …, Google = …)]`.
- **Two required arguments** (`apple`, `google`) in the new overload, so `Terms(uri)` can never bind to it.
- **A store's own link before the one for every store**, so `.Terms(uri).Terms(apple: …, google: null)` uses `uri` on Android.
- **`StoreLinks` in Common**, internal, so the Purchases tests (which reference only Common) can test it; `OperatingSystem.Is…` instead of `DeviceInfo`, which works at start-up and in a plain test run.
