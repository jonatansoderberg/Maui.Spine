# Purchases and subscriptions

**Status:** Proposal, 2026-11-18. P1 is built: the package, the paywall, the guard and the test store in #849, and the RevenueCat adapter in #852. P2 is built in #855: the server, with a credit ledger (consumables, free credits, spends) and RevenueCat's webhook. See `docs/wiki/purchases.md`. Spine facts are read from the source at `4f4c76c4`. Package facts come from the NuGet pages and the dotnet/maui issue on 2026-11-18.
**Question:** A free app wants to sell some features as a subscription or a one-time purchase. What exists for .NET MAUI today, what should Spine build, and in which packages?
**Answer:** Spine does not build its own store layer. It builds a thin package, `Plugin.Maui.Spine.Purchases`, on top of an existing store package. The package covers what Spine is good at:

- `IAppPurchases`, with typed keys from the app's own enums and no strings at the call site.
- `[RequiresPurchase]` on the guards from #506.
- A paywall sheet, a Purchases section for settings pages, and Pro marks on rows and page actions.
- A test store, so the Showcase and the tests work without App Store Connect or Play Console.

The store work goes to adapters. `.RevenueCat` is the first, built on `Plugin.RevenueCat`. `.InAppBilling` comes later, built on `Plugin.InAppBilling`, for Windows and for apps without RevenueCat. `Plugin.Maui.Spine.Server.Purchases` checks purchases on the server, because a guard is a gate in the UI, not a lock. The AI server's budgets can then depend on what the caller has bought.

---

## 1. What exists today

| Option | Platforms | Subscriptions | Server check | State on 2026-11-18 |
|---|---|---|---|---|
| **.NET MAUI itself** | – | – | – | [dotnet/maui#30658](https://github.com/dotnet/maui/issues/30658) was closed as not planned on 2025-12-10. The maintainers point to the BillingService sample and to `Plugin.RevenueCat`. |
| **[Plugin.InAppBilling](https://www.nuget.org/packages/Plugin.InAppBilling) 11.0.1** (James Montemagno), updated 2026-11-14 | net10.0: android36, ios26, maccatalyst26, macos26, windows10.0.19041 | Buys and lists subscriptions. Little on offers, upgrades, grace periods or expiry. | None | StoreKit 2 by default on iOS and Mac Catalyst. The page calls Android and Windows actively maintained and says nothing about iOS. |
| **[Plugin.RevenueCat](https://www.nuget.org/packages/Plugin.RevenueCat) 0.4.44** (Jonathan Dick, who leads RevenueCat's MAUI work), updated 2026-11-03 | net10.0: android36, ios26, maccatalyst26. No Windows. | All of RevenueCat: offerings, entitlements, trials, intro offers, customer info | In RevenueCat's service | A slim binding: only what the plugin needs is bound to the native SDKs. Still 0.x. |
| **[Kebechet.Maui.RevenueCat.InAppBilling](https://www.nuget.org/packages/Kebechet.Maui.RevenueCat.InAppBilling) 10.2.1**, updated 2026-11-17 | net9.0 builds (net10 apps resolve them). Windows is a stub. | Active subscriptions, trial eligibility, opening the store's subscription management | In RevenueCat's service | Frequent releases. Full bindings, so a larger surface to keep in step with the native SDKs. |
| **[Microsoft's BillingService sample](https://devblogs.microsoft.com/dotnet/cross-platform-billing-dotnet-maui/)** | iOS, Android | Basic | None | Code to copy, not a package. The iOS side is still StoreKit 1. |

### Why Spine does not own the store layer

- **StoreKit 2 has no Objective-C API.** Owning it means a Swift framework and bindings to it. Spine already ships Swift for widgets, so it can be done, but every StoreKit change becomes Spine's work.
- **Google retires Play Billing versions on a deadline**, about once a year. After the deadline an app on an old version cannot publish updates. Two maintained packages already follow those deadlines.
- **The hard part is not the purchase button.** It is the state behind it:
  - receipt checks, refunds, billing grace periods and account holds;
  - family sharing, upgrades and downgrades within a group;
  - a purchase that follows the user's account from iOS to Android.

  That is RevenueCat's whole product. Done without RevenueCat, it is a server project (§6).

## 2. Names

The name `Entitlement` is rejected. On Apple platforms *entitlements* are the signing capabilities in `Entitlements.plist`, and Spine's build and docs already use the word in that sense: App Groups, push, Sign in with Apple, associated domains.

| Name | Kept for | Why |
|---|---|---|
| **Purchase** | What the user has bought and the app checks: `Pro`, `RemoveAds`, `ThemePack` | It covers a subscription, a lifetime unlock and a one-time unlock. "The page requires the Pro purchase" reads as what it is. |
| **Product** | What the store sells: `ProMonthly`, `ProYearly`, `ProLifetime` | The same word App Store Connect, Play Console and Partner Center use. Several products can give the same purchase. |
| **`IAppPurchases`** | The service | "App" says these are the app's own purchases from the store, not purchases of anything in general. |
| **`Plugin.Maui.Spine.Purchases`** | The package | It also takes one-time purchases, so not `.Subscriptions`. |

Also rejected:

- **Subscription**: it leaves out lifetime and one-time unlocks.
- **Unlock**: it is taken by `[RequiresUnlock]` in Spine.Biometrics.
- **Plan** or **Tier**: they fit subscription levels but not "remove ads".

## 3. Typed keys, no strings at the call site

The app declares two enums in a project that both the app and its server can reference. The attributes that map the enums to the stores live in `Plugin.Maui.Spine.Common`, which has no MAUI dependency. Store ids are written once, on the enum members, and nowhere else.

```csharp
using Plugin.Maui.Spine.Purchases;

public enum AppPurchase
{
    [PurchaseId("pro")]               // the RevenueCat entitlement id; without the attribute it is the member's name
    Pro,

    [PurchaseId("no_ads")]
    RemoveAds,
}

public enum AppProduct
{
    [StoreProduct(ProductKind.Subscription, Apple = "se.example.notes.pro.month", Google = "pro:month", Microsoft = "9NBLGGH4R315")]
    [Grants(AppPurchase.Pro)]
    ProMonthly,

    [StoreProduct(ProductKind.Subscription, Apple = "se.example.notes.pro.year", Google = "pro:year")]
    [Grants(AppPurchase.Pro)]
    ProYearly,

    [StoreProduct(ProductKind.OneTime, Apple = "se.example.notes.pro.lifetime", Google = "pro_lifetime")]
    [Grants(AppPurchase.Pro, AppPurchase.RemoveAds)]
    ProLifetime,

    [StoreProduct(ProductKind.OneTime, Apple = "se.example.notes.noads", Google = "no_ads")]
    [Grants(AppPurchase.RemoveAds)]
    RemoveAds,
}
```

The kind is the constructor's required argument, so every product says what it is (§3.1).

- **`[Grants]`** is read by the test store, the `.InAppBilling` adapter and the server. With RevenueCat the same mapping lives in RevenueCat's dashboard. Spine checks it against the offerings at start-up and writes a `[Spine]` line for every product that RevenueCat maps differently, so the two never drift apart silently.
- **A product without an id for the running platform** is not offered there. The paywall leaves it out, and a call to `PurchaseAsync` with it throws a message that names the product and the platform.
- **The enums are registered once**, and the service's methods are generic over them, so a call reads `purchases.IsActive(AppPurchase.Pro)`. An enum that was not registered throws at the first call. The message names the enum and the registered ones: a typo must not turn into a feature that is silently free or silently locked.

```csharp
builder.UseSpinePurchases<AppPurchase, AppProduct>(o => o
    .UseRevenueCat(apiKeys: RevenueCatKeys.ForPlatform)   // or .UseInAppBilling(), or .UseTestStore()
    .Paywall(p => p
        .Offer(AppProduct.ProYearly, AppProduct.ProMonthly, AppProduct.ProLifetime)
        .Highlight(AppProduct.ProYearly)
        .Terms(new Uri("https://example.se/terms"))
        .Privacy(new Uri("https://example.se/privacy"))));
```

The RevenueCat keys are public SDK keys, one per platform. They are configuration, not code strings. The app keeps them where it keeps its other client ids.

### 3.1 Subscriptions and one-time purchases

The difference lies in the **product**, not in the purchase. The app always asks whether `AppPurchase.Pro` is active. Whether it came from a monthly subscription or a lifetime purchase matters only to the paywall, the settings row and the server. Each store sorts its products into the same few kinds:

| `ProductKind` | App Store | Google Play | Microsoft Store | Active |
|---|---|---|---|---|
| `Subscription` | Auto-renewable subscription, in a subscription group | Subscription with base plans (`pro:month`, `pro:year`) and offers | Subscription add-on | Until the period ends, or longer while the store retries the payment (grace period) |
| `OneTime` | Non-consumable | One-time product, acknowledged after the purchase | Durable add-on | For good, until a refund |
| `Pass` | Non-renewing subscription | One-time product | Durable add-on with a lifetime | For a fixed `Duration` given on the attribute. The store does not track it, so the server or the device counts the time from the purchase date. |
| `Consumable` | Consumable | One-time product, consumed after the purchase | Consumable add-on | Never "active". It adds to a balance (§8). |

The enum gives a place to all four kinds from the start. **P1 builds `Subscription` and `OneTime`.** `Pass` and `Consumable` throw "not supported yet" with the product's name until a phase builds them, so no app ships with one by accident. Every store package needs the kind:

- **`Plugin.InAppBilling`** takes it as `ItemType` on every call.
- **Play** has separate APIs and separate server endpoints for each kind: `purchases.subscriptionsv2` and `purchases.products`.
- **On Android**, a one-time purchase that is not acknowledged within three days is refunded. A consumable must be consumed before it can be bought again. The adapter does both from the kind, so an app cannot forget.

RevenueCat reads the kind from the store. There the attribute is checked against the store at start-up and a mismatch writes a `[Spine]` line, just like `[Grants]`.

**What the app sees.** `PurchaseState` says which kind gave the purchase:

```csharp
public sealed record PurchaseState(
    bool IsActive,
    ProductKind? Kind,                 // null when nothing has been bought
    DateTimeOffset? ExpiresAt,         // null for OneTime
    bool WillRenew,                    // Subscription only: false after the user has cancelled
    PurchasePhase Phase,               // Trial, IntroOffer, Regular, GracePeriod, BillingRetry
    Enum? Product);                    // the AppProduct that gave it, typed as the registered enum
```

**When both are bought.** A user can own `ProLifetime` and still pay for `ProMonthly`: they bought the subscription first and then took the lifetime offer. Both grant `Pro`, so the user pays for nothing. Spine handles it:

- **`IsActive` and `GetState`** take the strongest source: one-time before a subscription, then a subscription that renews before one that ends.
- **The paywall** leaves out subscription products for a purchase that a one-time purchase already gives.
- **The settings row** shows "Pro · lifetime", and under it a `NeedsAttention` row: "Your subscription still renews on 18 Dec. Cancel it in App Store." Its command is `ManageAsync`. The stores do not cancel the subscription themselves, and an app may not do it for the user.
- **`Changed`** reports the overlap once (`PurchasesChangedEventArgs.OverlappingSubscription`), so the app can say the same thing in its own way.

**Other differences the package covers:**

| | Subscription | One-time |
|---|---|---|
| The paywall's price | "99 kr/year", with "8.25 kr/month" under it, and the trial or intro offer | "299 kr once" |
| The primary button | "Start 7-day free trial", "Subscribe" | "Buy" |
| Upgrade and downgrade | Within the group or the subscription: the store prorates. `PurchaseAsync(AppProduct.ProYearly)` while `ProMonthly` is active is a change of plan, not a second purchase. On Play the adapter sends the old purchase token and a replacement mode. | Not applicable. Buying a one-time product that is already owned returns `Purchased` at once, without the store's sheet. |
| Restore | Restored, and renewals keep coming | Restored. Apple requires the Restore button for non-consumables and subscriptions alike. |
| Family Sharing (Apple) | Per product, if turned on in App Store Connect | Per product, if turned on in App Store Connect |
| A refund | Ends the subscription at once | Takes the purchase away. `Changed` is raised, and the server hears it from the notification (§6) |

## 4. The client package: `Plugin.Maui.Spine.Purchases`

### 4.1 `IAppPurchases`

```csharp
public interface IAppPurchases
{
    /// <summary>True while the purchase is active: a running or grace-period subscription, or a lifetime or one-time unlock.</summary>
    bool IsActive<TPurchase>(TPurchase purchase) where TPurchase : struct, Enum;

    /// <summary>What is known about a purchase: active, the kind of product that gave it, until when, renewing or not, trial, grace period (§3.1).</summary>
    PurchaseState GetState<TPurchase>(TPurchase purchase) where TPurchase : struct, Enum;

    /// <summary>Raised when the active purchases change: after a purchase, a restore, a renewal or an expiry the store reports, or a sign-in.</summary>
    event EventHandler<PurchasesChangedEventArgs> Changed;

    /// <summary>Prices, periods and trial eligibility of the products, in the store's currency and language.</summary>
    Task<IReadOnlyList<StoreProductInfo>> GetProductsAsync(CancellationToken cancellationToken = default);

    Task<PurchaseResult> PurchaseAsync<TProduct>(TProduct product, CancellationToken cancellationToken = default) where TProduct : struct, Enum;
    Task<PurchaseResult> RestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the store's own subscription management: App Store, Play, Microsoft Store.</summary>
    Task ManageAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the paywall sheet with every offered product. True when a purchase became active.</summary>
    Task<bool> ShowPaywallAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the paywall sheet with the products that grant <paramref name="purchase"/>. True when it is active afterwards.</summary>
    Task<bool> ShowPaywallAsync<TPurchase>(TPurchase purchase, CancellationToken cancellationToken = default) where TPurchase : struct, Enum;

    /// <summary>True at once when the purchase is active; otherwise opens the paywall for it and returns whether the user bought or restored it.</summary>
    Task<bool> RequireAsync<TPurchase>(TPurchase purchase, CancellationToken cancellationToken = default) where TPurchase : struct, Enum;
}
```

- **The last known state is kept** in `Preferences`. `IsActive` answers at once at launch and offline, and the store's answer arrives afterwards with a `Changed` event. Settings pages must open at once (`docs/wiki/settings-pages.md`), and so must a guarded page.
- **`PurchaseResult`** separates `Purchased`, `Pending` (Ask to Buy, Play's pending payments), `Cancelled` and `Failed`. `Failed` carries the store's code and message. A cancel is not an error and shows nothing.
- **When the user signs in with Spine.Authentication**, the adapter uses the Spine account id as the store's app user id. With RevenueCat that is `logIn`, so a purchase follows the account to the user's other devices. The package listens to `ISpineAuth.UserChanged` only when Authentication is registered; neither package references the other.

### 4.2 `[RequiresPurchase]`

A guard like `[RequiresSignIn]` and `[RequiresUnlock]`, registered with `AddSpineGuard`:

```csharp
[NavigableRegion(Title = "Statistics"), RequiresPurchase(AppPurchase.Pro)]
public partial class StatisticsPage { … }

[RelayCommand, PageAction(Icon = "Export.svg"), RequiresPurchase(AppPurchase.Pro)]
private Task ExportAsync() { … }
```

- **When the purchase is not active**, the paywall sheet opens for that purchase. After a purchase or a restore the navigation or command goes on, with its parameter. A cancel leaves the user where they were.
- **Order 150**: after sign-in (100) and before unlock (200), so the user is never asked for Face ID only to be shown a paywall.
- **The attribute takes the enum value as `object`**, since an attribute argument cannot be a type parameter. The guard checks the value against the registered enum, and a wrong one throws at the first check with its type and the registered ones.
- **On a guarded tab** on Android and Windows the guard's cover (`IGuardCover`) shows the purchase's name and a "See plans" button.
- **The guard does not cover a plain `Command="{Binding XCommand}"`**, as for every guard. `Purchase.Command` and `RequireAsync` cover those.

### 4.3 What a guard cannot do: marks and plain bindings

| Need | API |
|---|---|
| Show or hide a view by a purchase | `IsVisible="{spine:PurchaseActive {x:Static app:AppPurchase.RemoveAds}, Invert=True}"`. A markup extension that follows `Changed`. |
| Mark a row, a page action or a tab as Pro | `Purchase.Requires="{x:Static app:AppPurchase.Pro}"` on a `SpineRow`, a `[PageAction]` or a tab. It shows a small mark with the purchase's name from the app's strings while the purchase is not active, and the tap opens the paywall. |
| A button bound straight to a command | `Purchase.Command`, which wraps a command the way `Unlock.Command` does. |
| Code | `await purchases.RequireAsync(AppPurchase.Pro)`: true when the purchase is active or the user buys it now. |

The mark's text is the purchase's display name: `Purchases.Pro` in the app's strings, falling back to the member name. The strings are resolved through `ISpineStrings`, so the mark follows the app's language.

### 4.4 The paywall sheet

A `[NavigableSheet]` drawn by Spine from the `.Paywall(...)` options, in the same visual language as the rest of Spine:

- **A headline and the features**, as rows with icons, from the app's strings. Optionally a `MeshBackground` behind it.
- **The products**, subscriptions and one-time products side by side:
  - a subscription shows its price per period, the trial or intro offer the store says the user is eligible for, and a "per month" comparison under the yearly product;
  - a one-time product shows its price with "once".

  One product is highlighted. A subscription that a one-time purchase already gives is left out (§3.1).
- **A primary button in a footer.** It is the sheet's one primary action, as with Log in (see the sheet button conventions), and it says what happens for the selected product: "Start 7-day free trial", "Subscribe for 99 kr/year" or "Buy for 299 kr".
- **Restore purchases, Terms and Privacy** below the button. App Review guideline 3.1.2 requires a subscription paywall to show the price, the period, and links to the terms and the privacy policy. Missing links are the most common reason a paywall is rejected, so `.Paywall(...)` refuses to build without `Terms` and `Privacy`.
- **Close** is the sheet's own close button, with no confirmation.
- **The sheet opens at once.** Products come from the last known list and are updated after the sheet is up, as on settings pages. Before the first answer the rows show a shimmer; they do not jump in.

An app that wants its own paywall registers a page with `.Paywall<MyPaywallPage>()`. `ShowPaywallAsync` and the guard open it the same way.

RevenueCat's remote-configured paywalls (Paywalls v2) are native UI in separate SDKs. They are left out (§8).

### 4.5 The settings section

`docs/wiki/settings-pages.md` gets a **Purchases** section between Account (1) and what the app works with (2). It uses ready-made rows from the package:

| Row | Content |
|---|---|
| The purchase, one row per active purchase | `Status="Ok"` with what gave it: "Pro · renews 18 Dec 2026" or "Pro · ends 18 Dec 2026" for a subscription, "Pro · lifetime" or "No ads · bought" for a one-time purchase. A billing problem shows `NeedsAttention` with "Payment problem: update in App Store", and so does a subscription that still renews beside a one-time purchase (§3.1). A user who has bought nothing gets a row that opens the paywall. |
| Manage subscription | Only while a subscription is active or ending. It opens the store's management (`ManageAsync`) and has the external-link icon because it leaves the app. A one-time purchase has nothing to manage. |
| Restore purchases | `RestoreAsync`, with the result in the section's footer, not in an alert. |

The icons use the access colour, pink `#FF2D55`. The convention table gets the Purchases section added in the same PR.

### 4.6 The test store

`.UseTestStore()` keeps purchases in memory and in `Preferences`, follows `[Grants]`, and shows the paywall with prices from a `TestPrices` table in the options. Spine has no developer menu, so the test store gets an `ITestStore` with which a test, or a Debug-only settings row in the app, can expire, renew or refund a purchase, put it in a grace period, or make the next purchase answer `Pending`.

- **The Showcase sample runs on the test store**, so its page works on every platform without a store account.
- **The real store adapters are tried with:**
  - Apple's sandbox accounts on a device. A StoreKit configuration file in the simulator needs an Xcode scheme, and whether a MAUI build can use one is open (§9).
  - Play's license testers on the emulator and on a device.
- **A Release build with `UseTestStore()` fails to build** with a message, so a free Pro can never ship.

## 5. Adapters

| Adapter | On | Platforms | What it adds |
|---|---|---|---|
| `Plugin.Maui.Spine.Purchases.RevenueCat` (P1) | `Plugin.RevenueCat` | iOS, Mac Catalyst, Android | Offerings → products, entitlements → `PurchaseState`, `logIn`/`logOut` on sign-in, the customer info listener → `Changed`. |
| `Plugin.Maui.Spine.Purchases.InAppBilling` (P3) | `Plugin.InAppBilling` | iOS, Mac Catalyst, Android, Windows | The store calls, and `[Grants]` applied on the device. Without Spine's server (§6) the device's own state is the only answer: good enough for "remove ads", weak for anything the server pays for. |

Both adapters wrap the store package behind Spine's interface. An app never calls `Plugin.RevenueCat` or `Plugin.InAppBilling` directly, so changing adapter is a change in `MauiProgram`. Their versions are pinned in `Directory.Packages.props`, as for every dependency, and a new store package version goes through the Showcase on a device before a Spine release.

No `Plugin.RevenueCat` below 0.4.47: on Android, 0.4.44 read every whole number in RevenueCat's answers back as a double, so each Play purchase failed with `DeserializeUnableToConvertValue … System.Nullable`1[System.Int64] Path: $.customer_info.request_date_ms` after Play had charged. Spine has been on 0.4.52 since then, the version a Play license tester bought with in Hockeymatch.

## 6. The server: `Plugin.Maui.Spine.Server.Purchases`

The guard and the marks are UI only. Anything that costs money on the server must check the purchase there: AI calls, sync, storage, exports.

```csharp
builder.Services.AddSpinePurchases<AppPurchase, AppProduct>(o => o
    .UseRevenueCatWebhook(webhookSecretName: "RevenueCat:WebhookAuth")
    .Store(PurchaseStores.AzureTables));            // in memory for development, like the push and auth stores

app.MapSpinePurchases();                            // /spine/purchases/…

app.MapPost("/export", …).RequirePurchase(AppPurchase.Pro);
```

- **With RevenueCat:**
  - the server takes RevenueCat's webhook, checked against its authorization header, and keeps each user's purchases in the store;
  - when its stored state is older than a limit, it asks RevenueCat's REST API;
  - the user id is the Spine account id from Server.Authentication, or the installation id for a user who has not signed in.
- **Without RevenueCat** (with the `.InAppBilling` adapter), the server does the stores' work itself:
  - **Apple:** it checks StoreKit 2's signed transactions (JWS) against Apple's root certificate and takes App Store Server Notifications v2. Apple's App Store Server Library has no .NET version, so the JWS check is Spine's code. The transaction's `type` says the kind: auto-renewable, non-consumable, non-renewing or consumable. The server checks it against `[StoreProduct]`.
  - **Google:** it checks Play purchase tokens with the Android Publisher API through a service account, and takes Real-time Developer Notifications through Pub/Sub push. Subscriptions and one-time products have separate endpoints (`purchases.subscriptionsv2` and `purchases.products`) and separate notifications, and the kind on `[StoreProduct]` chooses between them.
  - **Both kinds are stored the same way:** a purchase with an expiry that is `null` for a one-time product. `RequirePurchase` and the AI budgets need not know which kind gave it.

  This is the largest part of the proposal and the reason it is P3.
- **`RequirePurchase(...)`** is an endpoint filter. It answers 403 with a problem body that names the purchase, so the app can open the paywall from the answer.
- **The AI server's budgets can follow what the caller has bought.** A budget's `PerCaller` already separates `SignedIn` and `Installation` callers (`AICallerBudgets`, `AIConfiguration.cs:346-366`). A per-purchase limit fits beside them, and the highest active purchase wins:

  ```csharp
  budget.PerCaller = new AICallerBudgets
  {
      Installation = new() { DayHard = 0.05m },
      SignedIn = new() { DayHard = 0.20m },
      ForPurchase = { [AppPurchase.Pro] = new() { DayHard = 2.00m } },
  };
  ```

## 7. Packages and phases

| Phase | Package | Content |
|---|---|---|
| **P1** | `Plugin.Maui.Spine.Common` | `[PurchaseId]`, `[StoreProduct]`, `[Grants]`, `PurchaseState` and the wire contracts shared with the server |
| **P1** | `Plugin.Maui.Spine.Purchases` | `IAppPurchases`, `[RequiresPurchase]`, `Purchase.Requires`, `Purchase.Command`, `{spine:PurchaseActive}`, the paywall, the settings rows, the test store |
| **P1** | `Plugin.Maui.Spine.Purchases.RevenueCat` | The RevenueCat adapter |
| **P1** | Showcase, wiki, skills | A purchases page on the test store, `docs/wiki/purchases.md`, the Purchases section in `settings-pages.md`, `spine-setup` and `spine-page` updated |
| **P2** | `Plugin.Maui.Spine.Server.Purchases` | The RevenueCat webhook, the store of purchases per user, `RequirePurchase`, per-purchase AI budgets |
| **P3** | `Plugin.Maui.Spine.Purchases.InAppBilling` and the server's own App Store and Play checks | Windows, and apps without RevenueCat |
| Later | `Plugin.Maui.Spine.Widgets` | Active purchases written to the App Group, so a widget can show "Unlock with Pro" |

## 8. Out of scope

- **RevenueCat's native paywalls** (Paywalls v2). They need RevenueCatUI bindings on both platforms and would draw a UI unlike the rest of the app.
- **Consumables and virtual currency** (coins, credits). Hockeymatch wants it first: one free live match per user, more matches bought one at a time, or a subscription for all of them. The free credit and the balance live on the server, so this comes with P2. `ProductKind.Consumable` has its place from the start (§3.1), but a consumable is a balance on the server, not an active purchase. It needs its own API (`[Adds(AppCredit.AiMinutes, 60)]` and a balance the server spends) and is better done when an app needs one.
- **Time-limited passes** (`ProductKind.Pass`, for example 30 days without renewal). Nothing else is needed for them except counting the time, but that count should live on the server, and P1 has no server.
- **Web purchases** through Stripe or RevenueCat Web Billing. The store rules on links out of the app differ per country and change often.
- **Promotional and win-back offers, offer codes and price-increase consent.** The adapter passes them through where the store package exposes them; Spine gets no UI for them in P1.

## 9. Open questions

1. **Names:** do *purchase* and *product*, `IAppPurchases` and `Plugin.Maui.Spine.Purchases` work, or is another word for what the app checks better (§2)?
2. **RevenueCat as the first adapter**, which ties P1 to a service that takes a share of revenue above a free limit, or `Plugin.InAppBilling` first, which gives Windows but no server check?
3. **Windows in P1?** Only the `.InAppBilling` adapter reaches the Microsoft Store.
4. **Can a MAUI simulator run use a StoreKit configuration file** without an Xcode scheme? This must be checked before writing the test instructions in the wiki.
5. **`Pass` with P2 or later?** A pass is a one-time product with an expiry that the server counts. It could come with the server in P2, or wait for an app that sells one.
6. **The guard's order (150):** paywall before unlock, but after sign-in. Should a purchase require sign-in by default, so it always follows an account, or stay anonymous until the user signs in?
