# Plugin.Maui.Spine.Purchases

Subscriptions and one-time purchases for a Spine app. `IAppPurchases` takes members of the app's own enums, `[RequiresPurchase]` opens a paywall before a page or a command, and `<PurchasesSection />` is the settings page's Purchases section. A test store sells everything on the device without a store account. The real store comes from an adapter package.

```bash
dotnet add package Plugin.Maui.Spine.Purchases
```

```csharp
public enum AppPurchase { [PurchaseId("pro")] Pro, Supporter }

public enum AppProduct
{
    [StoreProduct(ProductKind.Subscription, Apple = "se.example.app.pro.month", Google = "pro:month")]
    [Grants(AppPurchase.Pro)]
    ProMonthly,

    [StoreProduct(ProductKind.OneTime, Apple = "se.example.app.supporter", Google = "supporter")]
    [Grants(AppPurchase.Supporter)]
    Supporter,
}

builder.UseSpinePurchases<AppPurchase, AppProduct>(o => o
    .UseTestStore()
    .Paywall(p => p.Terms(new Uri("https://example.se/terms")).Privacy(new Uri("https://example.se/privacy"))));
```

```csharp
[NavigableRegion(Title = "Statistics"), RequiresPurchase(AppPurchase.Pro)]
public partial class StatisticsPage { … }

if (purchases.IsActive(AppPurchase.Pro)) …
await purchases.ShowPaywallAsync();
```

```xml
<Border IsVisible="{PurchaseActive {x:Static app:AppPurchase.Supporter}}">…thank you…</Border>
<PurchasesSection />
```

## Documentation

- [Purchases](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/purchases.md)
- [The proposal](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/proposals/spine-purchases.md)
