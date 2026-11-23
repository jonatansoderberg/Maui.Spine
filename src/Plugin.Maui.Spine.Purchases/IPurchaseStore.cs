namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// The store behind <see cref="IAppPurchases"/>: the test store, or an adapter package (RevenueCat, InAppBilling). An app
/// never calls it; it picks one in <c>UseSpinePurchases</c>.
/// </summary>
/// <remarks>
/// A store reports the products the user owns. Spine works out each purchase from them through <see cref="GrantsAttribute"/>,
/// keeps the last answer on the device, and draws the paywall and the settings rows.
/// </remarks>
public interface IPurchaseStore
{
    /// <summary>Where the user manages a subscription, for the settings rows: "App Store", "Google Play".</summary>
    string Name { get; }

    /// <summary>Raised when the store learns of a change by itself: a renewal, an expiry, a refund, an approved pending purchase.</summary>
    event EventHandler? OwnedChanged;

    /// <summary>The products the user owns now, including ended subscriptions the store still reports.</summary>
    Task<IReadOnlyList<OwnedProduct>> GetOwnedAsync(CancellationToken cancellationToken);

    /// <summary>The products of <paramref name="products"/> the store sells on this platform.</summary>
    Task<IReadOnlyList<StoreProductInfo>> GetProductsAsync(IReadOnlyList<ProductDefinition> products, CancellationToken cancellationToken);

    /// <summary>Buys <paramref name="product"/>. On a change of plan, <paramref name="replacing"/> is the active subscription it replaces.</summary>
    Task<PurchaseResult> PurchaseAsync(ProductDefinition product, OwnedProduct? replacing, CancellationToken cancellationToken);

    /// <summary>Asks the store for every purchase of the user's store account; <see cref="GetOwnedAsync"/> answers with them afterwards.</summary>
    Task RestoreAsync(CancellationToken cancellationToken);

    /// <summary>Opens the store's subscription management.</summary>
    Task ManageAsync(CancellationToken cancellationToken);
}
