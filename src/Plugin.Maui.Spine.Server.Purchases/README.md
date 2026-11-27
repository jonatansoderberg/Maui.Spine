# Plugin.Maui.Spine.Server.Purchases

Purchases on the server for Spine apps. It keeps a ledger per caller and holds:

- free credits;
- credits from consumables bought through RevenueCat;
- spends that unlock a reference once;
- the subscriptions and one-time products RevenueCat reports through its webhook.

`IPurchaseLedger` lets the app's own endpoints ask whether a purchase is active or a reference is unlocked. It works in ASP.NET Core and Azure Functions.

```bash
dotnet add package Plugin.Maui.Spine.Server.Purchases
```

```csharp
builder.Services.AddSpinePurchases<AppPurchase, AppProduct, AppCredit>(o =>
{
    o.UseAzureTableStore(builder.Configuration["Purchases:Storage"]!);
    o.RevenueCatWebhookAuthorization = builder.Configuration["RevenueCat:WebhookAuthorization"];
});

app.MapSpinePurchases();   // /purchases/credits, /purchases/credits/spend, /purchases/credits/forget, /purchases/revenuecat
```

## Documentation

- [Purchases: the server](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/purchases.md#the-server)
