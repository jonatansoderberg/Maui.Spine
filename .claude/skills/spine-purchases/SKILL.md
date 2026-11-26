---
name: spine-purchases
description: Sell subscriptions, one-time purchases and credits in a Spine app with Plugin.Maui.Spine.Purchases, the RevenueCat adapter and Plugin.Maui.Spine.Server.Purchases — the purchase/product/credit enums, the paywall, [RequiresPurchase], the settings section, the test store, the server ledger and RevenueCat's webhook, paid push tags — and the store side that takes most of the time: App Store Connect and Play Console products, RevenueCat apps, keys and notifications, sandbox and license testing, App Privacy and submitting the first purchases with a version. Use when an app starts selling something, adds a product, or is on its way to App Review with purchases. Invoke as /spine-purchases.
---

You are adding purchases to an app built on Spine, or getting one through the stores. The client package is **Plugin.Maui.Spine.Purchases** with the store adapter **Plugin.Maui.Spine.Purchases.RevenueCat**; the server package is **Plugin.Maui.Spine.Server.Purchases** (the ledger and RevenueCat's webhook). Full docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/purchases.md (the code) and https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/purchases-stores.md (the stores, RevenueCat and App Review).

Work in this order; each step is a stop the next one needs:

1. the enums and the client (below), on the **test store** first;
2. the accounts: agreements, bank and tax on both stores (the account holder does these; ask);
3. the products in App Store Connect and Play Console;
4. RevenueCat: project, apps, keys, entitlements, store notifications, the webhook;
5. the server and the keys in the build;
6. a sandbox purchase on iOS and a license-tester purchase on Play that reach the server's ledger;
7. App Review: the purchases go in with a version.

---

## The enums

In a project the app **and** the server reference (the attributes are in `Plugin.Maui.Spine.Common`, no MAUI):

```csharp
public enum AppPurchase                       // what the app checks
{
    [PurchaseId("pro")] Pro,                   // = the RevenueCat entitlement id
    Supporter,
}

public enum AppProduct                        // what the stores sell; the store ids live only here
{
    [StoreProduct(ProductKind.Subscription, Apple = "se.example.app.pro.year", Google = "pro:year")]   // Play: subscription:base-plan
    [Grants(AppPurchase.Pro)]
    ProYearly,

    [StoreProduct(ProductKind.OneTime, Apple = "se.example.app.supporter", Google = "supporter")]
    [Grants(AppPurchase.Supporter)]
    Supporter,

    [StoreProduct(ProductKind.Consumable, Apple = "se.example.app.match", Google = "match")]
    [Adds(AppCredit.LiveMatch, 1)]
    OneMatch,
}

public enum AppCredit                         // only for credits: spent one reference at a time
{
    [FreeCredits(1)] [UnlimitedWith(AppPurchase.Pro)]
    LiveMatch,
}
```

Apple's product ids are reverse-DNS and unique across the account; Play's are short and cannot be reused after deletion. Pick them once.

## The client

```csharp
builder.UseSpinePurchases<AppPurchase, AppProduct, AppCredit>(o => o     // after UseSpine(); drop AppCredit without credits
    .UseRevenueCat(r => { r.AppleApiKey = cfg["RevenueCat:Apple"]; r.GoogleApiKey = cfg["RevenueCat:Google"]; })   // public appl_/goog_ keys
    // or .UseTestStore(t => t.Price(AppProduct.ProYearly, 249).Period(AppProduct.ProYearly, StorePeriod.Year)) while there are no keys
    .Credits(c => c.Server = new Uri("https://api.example.se/purchases"))
    .Paywall(p => p
        .Headline("App Pro")
        .Feature("Graph.svg", "Statistics", "Every season")
        .ForCredit(AppCredit.LiveMatch, s => s.Headline("Follow the match live"))
        .For(AppPurchase.Supporter, s => s.Headline("Support the app"))
        .Highlight(AppProduct.ProYearly)
        .Terms(terms)                         // public pages, on every platform; start-up stops without them
        .Privacy(privacy)));
```

- **Check:** `purchases.IsActive(AppPurchase.Pro)` (at once, also offline), `GetState(…)`, `Changed`; `[RequiresPurchase(AppPurchase.Pro)]` on a page or a `[RelayCommand]`; `Purchase.Command` + `Purchase.Requires` in XAML; `{PurchaseActive {x:Static app:AppPurchase.Supporter}}` for visibility; `await purchases.RequireAsync(…)`.
- **Credits:** `await credits.UnlockAsync(AppCredit.LiveMatch, $"match:{id}")` spends once per reference, or opens the paywall with none left. `Balance`, `IsUnlocked`, `IsUnlimited`.
- **Settings:** `<PurchasesSection />` between Account and the app's own sections; icons with `o.Icon(AppPurchase.X, "Trophy.svg")`.
- **After a purchase:** `purchases.Purchased` (main thread, the paywall closed) for a welcome or a thank-you.
- **The app's words:** `.Describe(product, text)` under a price, `.Accent(color)`, and Spine's own strings overridden by key (`Spine.Purchases.Paywall.Once` = `{0} · engångsköp`).

## The server

```csharp
builder.Services.AddSpinePurchases<AppPurchase, AppProduct, AppCredit>(o =>
{
    o.UseAzureTableStore(cfg["Purchases:Storage"]!);
    o.RevenueCatWebhookAuthorization = cfg["Purchases:RevenueCatWebhookAuthorization"];   // a secret: key vault
});
app.MapSpinePurchases();   // GET /purchases/credits, POST /purchases/credits/spend, POST /purchases/revenuecat

if (await ledger.CallerAsync(request) is { } caller && await ledger.IsActiveAsync(caller, AppPurchase.Pro)) …
```

**Paid push tags** (`game:{id}`, `live:all`): `AllowTagsAsync` on the push server keeps a tag only when the ledger has paid for it (`CallerKeys.Installation(installation.Id)`). Put it behind a setting (for example `Purchases:CheckTags`) and turn it on only when **every** build in use sells through RevenueCat: a build on the test store sells without the server hearing of it, and checking would drop what it unlocked.

## The stores

Each of these cost a round trip in a real app:

- **Agreements first.** App Store Connect → Business: Paid Apps Agreement *Active*, bank, tax. Play: payments profile with the bank verified by a micro-deposit. They are the account holder's to sign and to enter; ask, and never type bank numbers or the deposit amount.
- **Products through the API** (App Store Connect API key, ES256 JWT): IAPs, subscription groups, prices, availability, trials, review screenshots. Write the script so it can run again: find what exists, make only what is missing.
- **Every product needs a review screenshot** of the paywall or it stays *Missing Metadata*. Take it on a phone (`xcrun devicectl device capture screenshot`): the simulator's sandbox storefront is the US one, so it shows dollars.
- **The app needs a price and availability** (`appPriceSchedules`, `/v2/appAvailabilities`) before a version can be submitted.
- **RevenueCat App Store app:** the In-App Purchase key is required (StoreKit 2 records nothing without it). **App Store Server Notifications**: RevenueCat's URL in App Store Connect → App Information, Production *and* Sandbox, Version 2. The URL holds a secret: the account holder pastes it.
- **RevenueCat Play Store app:** a service account with the Play permissions (allow an hour to propagate), and Google developer notifications (Pub/Sub) connected and pointed at from Play Console.
- **Entitlements** = the purchase enum's `[PurchaseId]`s, products attached by `[Grants]`. No offerings: Spine asks by id.
- **The webhook** to `/purchases/revenuecat` with the Authorization value; one webhook covers both stores. Check a test event answers 200.
- **Terms:** a public page. On iOS Apple's standard EULA works; it does not fit a Play build: link Google Play's terms of service there (`#if ANDROID`).

## Trying it

- **iOS:** a sandbox account under Settings → Developer → Sandbox Apple Account (iOS 26). Monthly renews in about 5 minutes. TestFlight builds use the sandbox.
- **Play:** a license tester and a build installed from a test track.
- **Done when** each store's purchase is in RevenueCat *and* in the server's ledger (a `txn:` row or an owned product). Look in the ledger's table, not only in RevenueCat.

## App Review

- **The first purchases go in with a version, through the web:** the version's *Add for Review*, then *Add for Review* on each purchase (and the **subscription group**) into the same draft, then *Submit for Review*. The API refuses (`FIRST_CONSUMABLE_MUST_BE_SUBMITTED_ON_VERSION`).
- **App Privacy:** at least Purchase History, and Device ID when the installation id is used; App Functionality, no tracking.
- **Age rating** (the 2026 questions `sharedLocation`, `datingAndRelationships` are booleans), **content rights**, China mainland left out unless wanted.
- **Review notes:** what each purchase unlocks, where it is, and that it can be bought in the sandbox. Ask before submitting anything.

## Do / Don't

- Do start on the test store, and keep the store ids only on the product enum.
- Do keep the enums in a project the server references too.
- Do ask the account holder before submitting for review, before turning on checks that read the ledger, and before anything involving bank, tax or agreements.
- Don't put RevenueCat's secret keys (`sk_…`), the webhook Authorization or the notification URL in the repository or a log; only the public `appl_`/`goog_` keys go in the build.
- Don't ship a Release build on the test store: start-up refuses unless `AllowTestStoreInRelease`, which is for builds that sell nothing.
- Don't link the paywall's terms into a private repository; App Review rejects it.

## Documentation

- The code: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/purchases.md
- The stores, RevenueCat and App Review: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/purchases-stores.md
- Sample: the Showcase's Purchases and Credits pages (https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples/MauiSpineSampleApp), on the test store
