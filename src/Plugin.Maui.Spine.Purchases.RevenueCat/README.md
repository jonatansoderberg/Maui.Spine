# Plugin.Maui.Spine.Purchases.RevenueCat

The RevenueCat store for `Plugin.Maui.Spine.Purchases` on iOS, Mac Catalyst and Android, built on [Plugin.RevenueCat](https://github.com/redth/Plugin.RevenueCat). It covers:

- products and prices from App Store and Google Play;
- purchases, with Play base plans and changes of plan;
- restores and the store's subscription management;
- the user's purchases across devices with the Spine account;
- a check of RevenueCat's entitlements against the app's `[Grants]`.

```bash
dotnet add package Plugin.Maui.Spine.Purchases.RevenueCat
```

```csharp
builder.UseSpinePurchases<AppPurchase, AppProduct>(o => o
    .UseRevenueCat(r =>
    {
        r.AppleApiKey = "appl_…";
        r.GoogleApiKey = "goog_…";
    })
    .Paywall(p => p.Terms(terms).Privacy(privacy)));
```

The RevenueCat project has one entitlement per member of the app's purchase enum, named by its `[PurchaseId]`, and the products with the ids on `[StoreProduct]`. Needs MAUI 10.0.110 or later.

## Documentation

- [Purchases: RevenueCat](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/purchases.md#revenuecat)
