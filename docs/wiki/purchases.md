# Purchases

`Plugin.Maui.Spine.Purchases` sells subscriptions and one-time purchases in a Spine app. It adds:

- `IAppPurchases`, which takes members of the app's own enums;
- `[RequiresPurchase]` on a page or a command;
- a paywall sheet;
- a Purchases section for the settings page;
- a test store.

The store itself comes from an adapter package. It also has credits (a balance spent one reference at a time) and a server package that keeps them and takes RevenueCat's webhook ([the proposal](../proposals/spine-purchases.md), P1 and P2).

```bash
dotnet add package Plugin.Maui.Spine.Purchases
```

## Purchases and products

The app declares two enums. A **purchase** is what the app checks: `Pro`, `Supporter`. A **product** is what the store sells: `ProMonthly`, `ProYearly`, `ProLifetime`. Several products can give the same purchase. The store ids are written once, on the product enum, and nowhere else.

```csharp
public enum AppPurchase
{
    [PurchaseId("pro")]          // the RevenueCat entitlement id; without it, the member's name
    Pro,

    Supporter,
}

public enum AppProduct
{
    [StoreProduct(ProductKind.Subscription, Apple = "se.example.app.pro.month", Google = "pro:month")]
    [Grants(AppPurchase.Pro)]
    ProMonthly,

    [StoreProduct(ProductKind.Subscription, Apple = "se.example.app.pro.year", Google = "pro:year")]
    [Grants(AppPurchase.Pro)]
    ProYearly,

    [StoreProduct(ProductKind.OneTime, Apple = "se.example.app.pro.lifetime", Google = "pro_lifetime")]
    [Grants(AppPurchase.Pro)]
    ProLifetime,

    [StoreProduct(ProductKind.OneTime, Apple = "se.example.app.supporter", Google = "supporter")]
    [Grants(AppPurchase.Supporter)]
    Supporter,
}
```

- **Every product says its kind.** `ProductKind.Subscription`, `ProductKind.OneTime` and `ProductKind.Consumable` (see [credits](#credits)) are supported. `Pass` throws "not supported yet" when bought.
- **The enums live in a project the server can reference too**, so the server can check the same purchases later. The attributes are in `Plugin.Maui.Spine.Common`, which has no MAUI dependency.

## Setup

Call it after `UseSpine()`:

```csharp
builder.UseSpinePurchases<AppPurchase, AppProduct>(o => o
    .UseTestStore(t => t
        .Price(AppProduct.ProMonthly, 29)
        .Price(AppProduct.ProYearly, 249)
        .Period(AppProduct.ProYearly, StorePeriod.Year)
        .Trial(AppProduct.ProYearly, StorePeriod.Days(7)))
    .Paywall(p => p
        .Offer(AppProduct.ProYearly, AppProduct.ProMonthly, AppProduct.ProLifetime, AppProduct.Supporter)
        .Highlight(AppProduct.ProYearly)
        .Headline("App Pro")
        .Feature("Graph.svg", "Statistics", "Every season")
        .For(AppPurchase.Supporter, s => s.Headline("Support the app").Feature("Heart.svg", "A thank-you banner"))
        .Terms(new Uri("https://example.se/terms"))
        .Privacy(new Uri("https://example.se/privacy"))));
```

The start-up stops with a message in these cases:

- there is no store;
- `Terms` or `Privacy` is missing. App Review rejects a subscription paywall without both links (guideline 3.1.2);
- a product has no `[StoreProduct]`;
- a `[Grants]` names something that is not a purchase;
- `UseSpinePurchases` is called twice, or before `UseSpine()`;
- a Release build is on the test store. Set `o.AllowTestStoreInRelease = true` for a build that sells nothing, as the Showcase does.

## Checking a purchase

```csharp
if (purchases.IsActive(AppPurchase.Pro)) …

var state = purchases.GetState(AppPurchase.Pro);
// state.Kind: Subscription or OneTime; state.ExpiresAt; state.WillRenew;
// state.Phase: Trial, IntroOffer, Regular, GracePeriod, BillingRetry; state.Product: the AppProduct that gave it

purchases.Changed += (_, e) => …;   // on the main thread, after a purchase, restore, renewal, expiry or refund
```

**`IsActive` answers at once**, also at launch and offline. The last known state is kept in `Preferences`, and the store's answer follows with `Changed`.

**The strongest source wins** when several products give the same purchase:

1. a one-time product before a subscription;
2. a subscription that renews before one that ends;
3. then the later end.

**A grace period keeps a subscription active** after its end while the store retries the payment. Billing retry does not.

## A page or a command behind a purchase

```csharp
[NavigableRegion(Title = "Statistics"), RequiresPurchase(AppPurchase.Pro)]
public partial class StatisticsPage { … }

[RequiresPurchase(AppPurchase.Pro)]
[RelayCommand]
private Task ExportAsync() { … }
```

- **Without the purchase**, the paywall opens with the products that give it. After a purchase or a restore the navigation or the command goes on; a cancel leaves the user where they were.
- **The guard's order is 150**: after `[RequiresSignIn]` (100) and before `[RequiresUnlock]` (200).
- **A guarded tab** gets a cover with the purchase's name and a "See plans" button.

A plain `Command="{Binding …}"` is not covered by the guard. Use `Purchase.Command` there, or `RequireAsync` in code:

```xml
<Button Text="Export" Purchase.Command="{Binding ExportCommand}" Purchase.Requires="{x:Static app:AppPurchase.Pro}" />

<SpineRow Title="Statistics" Purchase.Command="{Binding OpenStatisticsCommand}" Purchase.Requires="{x:Static app:AppPurchase.Pro}" />
```

```csharp
if (await purchases.RequireAsync(AppPurchase.Pro)) …
```

**On a `SpineRow`**, `Purchase.Requires` shows the purchase's name as a pink mark in the accessory while it is not active. The tap goes through the paywall only with `Purchase.Command`. A row's own bound `Command` cannot be wrapped, because setting it from code would drop the binding.

## Showing or hiding by a purchase

```xml
<Border IsVisible="{PurchaseActive {x:Static app:AppPurchase.Supporter}}">…thank you…</Border>
<Button IsVisible="{PurchaseActive {x:Static app:AppPurchase.Supporter}, Invert=True}" Text="Support the app" />
```

`{PurchaseActive}` is a binding that follows `Changed`. In an `Example`'s `Summary` or any attribute text, write `{}{PurchaseActive}` so XAML does not read it as markup.

## The paywall

`ShowPaywallAsync()` opens it with every offered product. `ShowPaywallAsync(purchase)` and the guard open it with only the products that give that purchase, and with that purchase's own text from `.For(…)` when there is one.

| Product | Price line | Detail | Button |
|---|---|---|---|
| Subscription with a trial | 249 kr/år | 7 dagar gratis · 20,75 kr/mån | Prova 7 dagar gratis, and under it "Then 249 kr/year. Cancel at any time in App Store." |
| Subscription | 29 kr/mån | – | Subscribe for 29 kr/month, or Switch for … while another plan of the same purchase runs |
| One-time | 599 kr en gång | – | Buy for 599 kr |

- **Owned products are shown dimmed**, marked "Your plan" or "Bought".
- **A subscription is left out** when a one-time purchase already gives everything it gives.
- **Restore purchases, Terms and Privacy** are under the products.
- **Text in the app's language:** the product's name is `Purchases.Products.<Member>` in the app's strings and the purchase's name is `Purchases.<Member>`. Without them, the member name is split at capitals ("Pro Yearly").

An app with its own paywall registers it with `.Paywall<MyPaywallPage>()`: a `[NavigableSheet]` that takes a `PaywallRequest` and returns `bool`.

## The settings section

```xml
<PurchasesSection />
```

Put it between Account and what the app works with ([settings pages](settings-pages.md)). It has:

- **a row per active purchase**: "Pro Yearly · Renews 30 Nov 2026", "Lifetime" or "Bought", with `Status="Ok"`. A grace period shows `NeedsAttention` with "Payment problem: update it in App Store";
- **"Get Supporter"** for each purchase not bought yet, which opens its paywall;
- **a row for an overlapping subscription**, `NeedsAttention`: it still renews although a one-time purchase gives the same;
- **Manage subscription** (`ExternalLink`, indigo) while a subscription runs;
- **Restore purchases**, with the result in the section's footer.

## The test store

`UseTestStore()` keeps purchases on the device and asks "Buy … for …? Nothing is charged." before each purchase, as the store's own sheet would. `ITestStore` sends the events a real store sends, now:

```csharp
store.Expire(AppProduct.ProMonthly);          // the period is over
store.Renew(AppProduct.ProMonthly);           // another period
store.Cancel(AppProduct.ProMonthly);          // renewal off; active to the end
store.StartGracePeriod(AppProduct.ProMonthly);
store.Refund(AppProduct.ProLifetime);
store.NextPurchasePending = true;             // Ask to Buy; store.ApprovePending() lets it through
store.NextPurchaseFails = true;
store.Reset();
```

A trial is given once per product, as the stores do. `Reset()` forgets it.

## Credits

Credits are a balance a user spends on something one at a time: a live match, an export. The app declares a third enum
for them, and its products add to it:

```csharp
public enum AppCredit
{
    [FreeCredits(1)]                    // every caller gets one, once
    [UnlimitedWith(AppPurchase.Live)]   // spending is free while Live is active
    LiveMatch,
}

[StoreProduct(ProductKind.Consumable, Apple = "se.example.app.match", Google = "match")]
[Adds(AppCredit.LiveMatch, 1)]
OneMatch,
```

```csharp
builder.UseSpinePurchases<AppPurchase, AppProduct, AppCredit>(o => o
    .UseRevenueCat(r => { … })
    .Credits(c => c.Server = new Uri("https://api.example.se/purchases"))
    .Paywall(p => p
        .ForCredit(AppCredit.LiveMatch, s => s.Headline("Follow more matches live"))
        .Terms(terms).Privacy(privacy)));
```

```csharp
if (await credits.UnlockAsync(AppCredit.LiveMatch, $"match:{match.Id}"))
    await FollowLiveAsync(match);

credits.Balance(AppCredit.LiveMatch);              // from the last answer, at once
credits.IsUnlocked(AppCredit.LiveMatch, "match:1");
credits.IsUnlimited(AppCredit.LiveMatch);
```

**What `UnlockAsync` does:**

1. When the reference is unlocked already, or the credit is unlimited, it answers true at once.
2. Otherwise it spends one credit, once per reference: asking twice for `match:1` takes one credit.
3. With none left, it opens the paywall for the credit. The paywall offers the products that add the credit and the purchases that make it unlimited. After a purchase it spends again.

**Where the ledger lives:**

- **On the app's server** (`c.Server`): every free grant, purchase and spend is a row written once by its id. A purchase reaches the ledger through RevenueCat's webhook. After a consumable purchase the app asks the server until the credits are there.
- **On the device** with the test store and no server: the same ledger in `Preferences`. The Showcase's "Credits" example runs on it.

**Who the caller is:** the signed-in Spine user (`user:{id}`), or otherwise the app installation (`installation:{id}`). The RevenueCat adapter uses the same key as RevenueCat's app user id, so a purchase lands in the right ledger.

- **A free credit follows the caller.** An installation that later signs in is a new caller and gets another one. Tie the free credit to an account by requiring sign-in for the spend.
- **On iOS**, Apple's DeviceCheck can keep a bit per device that survives a reinstall. Spine does not use it yet.
- **Bought credits cannot be restored from the store**, as consumables never can. They are in the server's ledger and follow the account.

## The server

`Plugin.Maui.Spine.Server.Purchases` keeps the ledger, takes RevenueCat's webhook, and lets the app's own endpoints ask about a caller's purchases and credits:

```csharp
builder.Services.AddSpinePurchases<AppPurchase, AppProduct, AppCredit>(o =>
{
    o.UseAzureTableStore(builder.Configuration["Purchases:Storage"]!);   // or o.UseInMemoryStore()
    o.RevenueCatWebhookAuthorization = builder.Configuration["RevenueCat:WebhookAuthorization"];
});

app.MapSpinePurchases();   // GET /purchases/credits, POST /purchases/credits/spend, POST /purchases/revenuecat
```

```csharp
app.MapGet("/matches/{id}/live", async (string id, HttpRequest request, IPurchaseLedger ledger) =>
    await ledger.CallerAsync(request) is { } caller && await ledger.IsUnlockedAsync(caller, AppCredit.LiveMatch, $"match:{id}")
        ? Results.Ok(…)
        : Results.StatusCode(402));

await ledger.IsActiveAsync(caller, AppPurchase.Pro);           // from what RevenueCat has reported
await ledger.GrantAsync(caller, AppCredit.LiveMatch, 5, "demo");   // once per id: a gift, a demo account
```

- **The enums** live in a project both the app and the server reference, so both read the same `[StoreProduct]`, `[Grants]`, `[Adds]`, `[FreeCredits]` and `[UnlimitedWith]`.
- **In RevenueCat**, add a webhook to `https://…/purchases/revenuecat` with an Authorization value, and give the server the same value. The webhook writes:
  - consumables as credits, once per transaction;
  - subscriptions as owned products, through purchase, renewal, cancellation, expiration, billing issue and refund;
  - one-time products as owned for good.
- **Events it skips, with a log line:** an event for an anonymous RevenueCat user, or for a product no enum member names.
- **The caller** is the JWT `sub` (Spine sign-in), or the `Spine-Installation` header. `o.Identify` replaces that.
- **Spends:** two at the same time for one caller are serialised within one server instance. Two spends on different references at the same moment on two instances could both take the last credit.

## RevenueCat

`Plugin.Maui.Spine.Purchases.RevenueCat` sells through App Store and Google Play with RevenueCat, on [Plugin.RevenueCat](https://github.com/redth/Plugin.RevenueCat). It runs on iOS, Mac Catalyst and Android; Plugin.RevenueCat has no Windows build. It needs MAUI 10.0.110 or later.

```bash
dotnet add package Plugin.Maui.Spine.Purchases.RevenueCat
```

```csharp
builder.UseSpinePurchases<AppPurchase, AppProduct>(o => o
    .UseRevenueCat(r =>
    {
        r.AppleApiKey = "appl_…";    // public SDK keys from the RevenueCat project's API keys, not secret keys
        r.GoogleApiKey = "goog_…";
    })
    .Paywall(p => p.Terms(terms).Privacy(privacy)));
```

**Setting up a project:**

1. Create the products in App Store Connect and in Play Console with the ids on `[StoreProduct]`. A Play subscription is `subscription:base-plan`, for example `pro:month`. In App Store Connect the Paid Applications Agreement, bank and tax forms must be done first.
2. In RevenueCat, add the App Store app and the Play Store app and import the products. Make one entitlement per member of the purchase enum, with the member's `[PurchaseId]` as its identifier (`pro`), and attach the products that give it.
3. Put the two public SDK keys in the app's build, as the Showcase does with `-p:SpineRevenueCatAppleKey=appl_…`.

**What the adapter does:**

- **Owned products** come from RevenueCat's customer info: subscriptions with their end, renewal, trial, intro offer, grace period and billing retry, and one-time products. A refunded subscription gives nothing. Spine works out the purchases through `[Grants]`.
- **Entitlements are checked against `[Grants]`.** When RevenueCat gives an entitlement for a product whose `[Grants]` does not give it, or has an entitlement that is no member of the enum, a `[Spine]` warning is logged once. Spine follows `[Grants]`.
- **Products and prices** come from the store through RevenueCat, by id; offerings are not needed. A free trial shows only when the store says the user is still eligible for it (Apple's intro eligibility, Play's offer).
- **On Play, a base plan is bought through its subscription option**, because a purchase by product id buys the subscription's first base plan. A change of plan passes the old product with time proration.
- **The RevenueCat app user id follows the app's user:**
  - the signed-in Spine user's id when Plugin.Maui.Spine.Authentication is used;
  - the user `r.AppUserId` returns, when the app sets it;
  - otherwise RevenueCat's anonymous id.

  It is checked before every look at the purchases, so a sign-in or sign-out takes effect on the next refresh.
- **Manage subscription** opens RevenueCat's management URL, or the store's subscription page.
- **Offline or with a store error**, the last known state stays, and the paywall shows the last known products. Spine keeps that state per store, so the test store's purchases never show up as RevenueCat's.

**Trying it:**

- **Apple:** a sandbox account on a device, or a StoreKit configuration file. Whether a MAUI simulator run can use a StoreKit configuration file is still open (proposal §9).
- **Google:** a license tester on a device or an emulator with Play.

## Writing a store adapter

An adapter implements `IPurchaseStore` and calls `o.UseStore((services, catalog) => new MyStore(…))` from its own `Use…()` extension:

- it reports the products the user owns (`OwnedProduct`: product, purchase date, end, renews, phase);
- Spine works out every purchase from them through `[Grants]`;
- `PurchaseCatalog` maps store ids back to the app's enum members (`ProductForStoreId`).

## Terms

<a id="terms"></a><a id="privacy"></a>The Showcase links here for its terms and privacy policy. It sells nothing, and the test store keeps every purchase on the device.

## See also

- [The proposal: purchases and subscriptions](../proposals/spine-purchases.md)
- [Guards](guards.md)
- [Settings pages](settings-pages.md)
- The Purchases page in Spine Showcase
