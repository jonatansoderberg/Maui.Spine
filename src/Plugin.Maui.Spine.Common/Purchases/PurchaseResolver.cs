namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// Works out each purchase's <see cref="PurchaseState"/> from the products the user owns. The strongest source wins: a
/// one-time product before a subscription, a subscription that renews before one that ends, then the later end.
/// </summary>
public static class PurchaseResolver
{
    /// <summary>The state of every purchase in <paramref name="catalog"/>.</summary>
    public static IReadOnlyDictionary<Enum, PurchaseState> Resolve(PurchaseCatalog catalog, IEnumerable<OwnedProduct> owned, DateTimeOffset now)
    {
        var products = owned.ToList();
        var states = new Dictionary<Enum, PurchaseState>();

        foreach (var purchase in catalog.Purchases)
        {
            var best = products
                .Select(o => (Owned: o, Product: catalog.Product(o.Product)))
                .Where(x => x.Product.Grants.Contains(purchase))
                .Select(x => (x.Owned, x.Product, Active: IsActive(x.Owned, x.Product.Kind, now)))
                .OrderByDescending(x => x.Active)
                .ThenBy(x => x.Product.Kind == ProductKind.OneTime ? 0 : 1)
                .ThenByDescending(x => x.Owned.WillRenew)
                .ThenByDescending(x => x.Owned.ExpiresAt ?? DateTimeOffset.MaxValue)
                .FirstOrDefault();

            states[purchase.Value] = best.Product is null
                ? PurchaseState.None
                : new PurchaseState(best.Active, best.Product.Kind, best.Product.Kind == ProductKind.OneTime ? null : best.Owned.ExpiresAt,
                    best.Product.Kind == ProductKind.Subscription && best.Owned.WillRenew, best.Owned.Phase, best.Owned.Product);
        }

        return states;
    }

    /// <summary>
    /// The renewing subscriptions that give nothing because a one-time product already gives all of it: the user pays for
    /// nothing and should cancel them in the store.
    /// </summary>
    public static IReadOnlyList<OwnedProduct> OverlappingSubscriptions(PurchaseCatalog catalog, IEnumerable<OwnedProduct> owned, DateTimeOffset now)
    {
        var products = owned.ToList();
        var lifetime = products
            .Where(o => catalog.Product(o.Product).Kind == ProductKind.OneTime)
            .SelectMany(o => catalog.Product(o.Product).Grants)
            .ToHashSet();

        return products
            .Where(o => catalog.Product(o.Product) is { Kind: ProductKind.Subscription, Grants: { Count: > 0 } grants }
                && o.WillRenew && IsActive(o, ProductKind.Subscription, now) && grants.All(lifetime.Contains))
            .ToList();
    }

    /// <summary>True while an owned product gives what it grants.</summary>
    public static bool IsActive(OwnedProduct owned, ProductKind kind, DateTimeOffset now) => kind switch
    {
        ProductKind.OneTime => true,
        ProductKind.Subscription => owned.Phase == PurchasePhase.GracePeriod
            || owned.Phase != PurchasePhase.BillingRetry && owned.ExpiresAt is { } end && end > now,
        _ => false,
    };
}
