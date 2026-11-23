#if !WINDOWS
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.RevenueCat;
using Plugin.RevenueCat.Models;

namespace Plugin.Maui.Spine.Purchases.RevenueCat;

/// <summary>
/// <see cref="IPurchaseStore"/> over <see cref="IRevenueCatManager"/>: the owned products from RevenueCat's customer info,
/// products and prices from the store through RevenueCat, and the RevenueCat app user id kept in step with the app's user.
/// </summary>
internal sealed class RevenueCatStore : IPurchaseStore
{
    private readonly IRevenueCatManager _revenueCat;
    private readonly PurchaseCatalog _catalog;
    private readonly Func<IServiceProvider, ValueTask<string?>> _appUserId;
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly StorePlatform _platform = OperatingSystem.IsAndroid() ? StorePlatform.Google : StorePlatform.Apple;
    private readonly ConcurrentDictionary<string, StoreProduct> _storeProducts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _warned = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _user = new(1, 1);

    public RevenueCatStore(IRevenueCatManager revenueCat, PurchaseCatalog catalog, Func<IServiceProvider, ValueTask<string?>> appUserId, IServiceProvider services, ILogger logger)
    {
        _revenueCat = revenueCat;
        _catalog = catalog;
        _appUserId = appUserId;
        _services = services;
        _logger = logger;
        _revenueCat.CustomerInfoUpdated += (_, _) => OwnedChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Name => SpineStrings.Current[_platform == StorePlatform.Google ? "Spine.Purchases.Store.Google" : "Spine.Purchases.Store.Apple"];

    public event EventHandler? OwnedChanged;

    public async Task<IReadOnlyList<OwnedProduct>> GetOwnedAsync(CancellationToken cancellationToken)
    {
        await PrepareAsync();
        var result = await _revenueCat.GetCustomerInfoWithResultAsync(false);
        if (!result.IsSuccess || result.Value is not { } info)
            throw Failure("customer info", result.Error);

        CheckEntitlements(info);
        return Owned(info);
    }

    public async Task<IReadOnlyList<StoreProductInfo>> GetProductsAsync(IReadOnlyList<ProductDefinition> products, CancellationToken cancellationToken)
    {
        await PrepareAsync();
        var list = new List<StoreProductInfo>();

        foreach (var group in products.GroupBy(p => p.Kind))
        {
            var ids = group.Select(p => p.IdFor(_platform)).OfType<string>().ToList();
            if (ids.Count == 0)
                continue;

            // Play sells a subscription's base plans under the subscription's id: ask for "pro", get "pro:month" and "pro:year".
            var query = ids.Select(BaseId).Distinct().ToList();
            var type = group.Key == ProductKind.Subscription ? RevenueCatProductType.Subscription
                : _platform == StorePlatform.Google ? RevenueCatProductType.InApp : (RevenueCatProductType?)null;

            var result = await _revenueCat.GetProductsWithResultAsync(query, type);
            if (!result.IsSuccess || result.Value is not { } found)
                throw Failure("products", result.Error);

            foreach (var product in group)
            {
                if (product.IdFor(_platform) is not { } id || found.FirstOrDefault(s => s.Identifier == id) is not { } store)
                    continue;

                _storeProducts[id] = store;
                list.Add(new StoreProductInfo(
                    product.Value,
                    product.Kind,
                    store.Title,
                    store.PriceString,
                    Amount(store.Price),
                    store.CurrencyCode,
                    product.Kind == ProductKind.Subscription ? Period(store.SubscriptionPeriod ?? store.DefaultSubscriptionOption?.BillingPeriod) : null,
                    product.Kind == ProductKind.Subscription ? await FreeTrialAsync(store) : null));
            }
        }

        foreach (var product in products.Where(p => p.IdFor(_platform) is { } id && !list.Any(i => Equals(i.Product, p.Value))))
            Warn($"product:{product.Value}", $"[Spine] Purchases: {_catalog.ProductType.Name}.{product.Value} ({product.IdFor(_platform)}) was not found in the store through RevenueCat. Check the id in App Store Connect or Play Console and that the product is in the RevenueCat project.");

        return list;
    }

    public async Task<PurchaseResult> PurchaseAsync(ProductDefinition product, OwnedProduct? replacing, CancellationToken cancellationToken)
    {
        await PrepareAsync();
        var id = product.IdFor(_platform)
            ?? throw new InvalidOperationException($"Spine purchases: {_catalog.ProductType.Name}.{product.Value} has no {_platform} id in [StoreProduct], so it is not sold here.");

        object? context = null;
#if ANDROID
        context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
#endif
        var options = replacing is not null && _platform == StorePlatform.Google && _catalog.Product(replacing.Product).IdFor(_platform) is { } old
            ? new PurchaseOptions { OldProductIdentifier = BaseId(old), ReplacementMode = RevenueCatReplacementMode.WithTimeProration }
            : null;

        RevenueCatOperationResult<Plugin.RevenueCat.Models.PurchaseResult> result;
        if (_platform == StorePlatform.Google && product.Kind == ProductKind.Subscription)
        {
            // A Play purchase by product id buys its first base plan; the subscription option picks this one, with the offer
            // (a trial) Play gives the user for it.
            var store = _storeProducts.TryGetValue(id, out var known) ? known : null;
            var option = store?.DefaultSubscriptionOption?.Id ?? PlanId(id);
            result = await _revenueCat.PurchaseSubscriptionOptionWithOperationResultAsync(context, BaseId(id), option, RevenueCatProductType.Subscription, options);
        }
        else
        {
            var type = product.Kind == ProductKind.Subscription ? RevenueCatProductType.Subscription
                : _platform == StorePlatform.Google ? RevenueCatProductType.InApp : (RevenueCatProductType?)null;
            result = await _revenueCat.PurchaseProductWithOperationResultAsync(context, id, type, options);
        }

        if (result.UserCancelled || result.Value?.UserCancelled == true)
            return PurchaseResult.Cancelled;
        if (result.IsSuccess)
            return PurchaseResult.Purchased;
        if (result.Error?.Code is { } code && code.Contains("PaymentPending", StringComparison.OrdinalIgnoreCase))
            return PurchaseResult.Pending;

        _logger.LogWarning("Spine purchases: RevenueCat refused {Product}: {Code} {Message}", id, result.Error?.Code, result.Error?.Message);
        return PurchaseResult.Failed(result.Error?.Code, result.Error?.Message);
    }

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        await PrepareAsync();
        var result = await _revenueCat.RestoreWithResultAsync();
        if (!result.IsSuccess)
            throw Failure("restore", result.Error);
    }

    public async Task ManageAsync(CancellationToken cancellationToken)
    {
        await PrepareAsync();
        var info = await _revenueCat.GetCustomerInfoAsync(false);
        var url = info?.ManagementUrl ?? info?.Subscriber?.ManagementUrl ?? (_platform == StorePlatform.Google
            ? "https://play.google.com/store/account/subscriptions"
            : "https://apps.apple.com/account/subscriptions");
        await Launcher.Default.OpenAsync(new Uri(url));
    }

    /// <summary>
    /// Starts the SDK if its own start-up hook has not run yet (Spine refreshes from an IMauiInitializeService, which runs
    /// first), and logs the RevenueCat user in or out to match the app's user.
    /// </summary>
    private async Task PrepareAsync()
    {
        if (string.IsNullOrEmpty(_revenueCat.ApiKey))
            _revenueCat.Initialize();

        var wanted = await _appUserId(_services);
        if (wanted == _revenueCat.AppUserId || wanted is null && _revenueCat.IsAnonymous)
            return;

        await _user.WaitAsync();
        try
        {
            if (wanted is not null && wanted != _revenueCat.AppUserId)
            {
                var login = await _revenueCat.LoginWithResultAsync(wanted);
                if (!login.IsSuccess)
                    _logger.LogWarning("Spine purchases: RevenueCat could not log in the app's user: {Message}", login.Error?.Message);
            }
            else if (wanted is null && !_revenueCat.IsAnonymous)
            {
                var logout = await _revenueCat.LogOutWithResultAsync();
                if (!logout.IsSuccess)
                    _logger.LogWarning("Spine purchases: RevenueCat could not log out: {Message}", logout.Error?.Message);
            }
        }
        finally
        {
            _user.Release();
        }
    }

    private List<OwnedProduct> Owned(CustomerInfo info)
    {
        var now = DateTimeOffset.UtcNow;
        var owned = new List<OwnedProduct>();

        foreach (var (key, subscription) in info.Subscriber?.Subscriptions ?? new Dictionary<string, Subscription>())
        {
            if (subscription.RefundedAt is not null || Product(key, subscription.ProductPlanIdentifier) is not { } product)
                continue;

            var phase = subscription.BillingIssuesDetectedAt is not null
                ? subscription.GracePeriodExpiresDate is { } grace && grace > now ? PurchasePhase.GracePeriod : PurchasePhase.BillingRetry
                : subscription.PeriodType?.ToLowerInvariant() switch
                {
                    "trial" => PurchasePhase.Trial,
                    "intro" => PurchasePhase.IntroOffer,
                    _ => PurchasePhase.Regular,
                };
            var willRenew = subscription.WillRenew ?? subscription.UnsubscribeDetectedAt is null;

            owned.Add(new OwnedProduct(product.Value, subscription.PurchaseDate ?? subscription.OriginalPurchaseDate ?? now,
                subscription.ExpiresDate, willRenew, phase));
        }

        foreach (var (key, purchases) in info.Subscriber?.NonSubscriptions ?? new Dictionary<string, List<NonSubscription>>())
        {
            // A consumable is used up: its credits are in the server's ledger, not among the owned products.
            if (Product(key, null) is not { } product || product.Kind == ProductKind.Consumable || purchases.Count == 0)
                continue;

            var first = purchases.Min(p => p.PurchaseDate) ?? now;
            owned.Add(new OwnedProduct(product.Value, first, null, false));
        }

        return owned;
    }

    /// <summary>RevenueCat's entitlements and the app's [Grants] must agree; a difference is a free or a locked feature.</summary>
    private void CheckEntitlements(CustomerInfo info)
    {
        foreach (var (id, entitlement) in info.Subscriber?.Entitlements ?? new Dictionary<string, Entitlement>())
        {
            if (entitlement.ExpiresDate is { } end && end < DateTimeOffset.UtcNow)
                continue;

            var purchase = _catalog.Purchases.FirstOrDefault(p => p.Id == id);
            if (purchase is null)
            {
                Warn($"entitlement:{id}", $"[Spine] Purchases: RevenueCat's entitlement '{id}' is no member of {_catalog.PurchaseType.Name}. Give the member [PurchaseId(\"{id}\")], or remove the entitlement.");
                continue;
            }

            if (entitlement.ProductIdentifier is { } productId && Product(productId, entitlement.ProductPlanIdentifier) is { } product && !product.Grants.Contains(purchase))
                Warn($"grant:{id}:{productId}", $"[Spine] Purchases: RevenueCat gives '{id}' for {productId}, but {_catalog.ProductType.Name}.{product.Value} has no [Grants({_catalog.PurchaseType.Name}.{purchase.Value})]. Spine follows [Grants]; make the two agree.");
        }
    }

    private ProductDefinition? Product(string storeId, string? plan)
    {
        var product = _catalog.ProductForStoreId(_platform, storeId)
            ?? (plan is not null ? _catalog.ProductForStoreId(_platform, $"{storeId}:{plan}") : null);

        if (product is null)
            Warn($"unknown:{storeId}:{plan}", $"[Spine] Purchases: the user owns {storeId}{(plan is null ? "" : ":" + plan)}, which no member of {_catalog.ProductType.Name} has as its {_platform} id. It gives nothing.");
        return product;
    }

    private async Task<StorePeriod?> FreeTrialAsync(StoreProduct store)
    {
        if (_platform == StorePlatform.Google)
            return Period(store.DefaultSubscriptionOption?.FreePhase?.BillingPeriod);

        if (store.IntroductoryDiscount is not { } intro
            || !(intro.Price?.Amount == 0 || intro.PaymentMode?.Contains("free", StringComparison.OrdinalIgnoreCase) == true)
            || Period(intro.SubscriptionPeriod) is not { } period)
            return null;

        // A trial is offered once per subscription group; the store says whether this user has had it.
        if (_revenueCat is IRevenueCatIntroEligibility eligibility)
        {
            var result = await eligibility.CheckTrialOrIntroDiscountEligibilityWithResultAsync([store.Identifier]);
            if (result.IsSuccess && result.Value?.GetValueOrDefault(store.Identifier) is not IntroEligibilityStatus.Eligible)
                return null;
        }

        var times = Math.Max(1, intro.NumberOfPeriods ?? 1);
        return period with { Count = period.Count * times };
    }

    private static StorePeriod? Period(SubscriptionPeriod? period) => period?.Unit switch
    {
        SubscriptionPeriodUnit.Day => new StorePeriod(period.Value, StorePeriodUnit.Day),
        SubscriptionPeriodUnit.Week => new StorePeriod(period.Value, StorePeriodUnit.Week),
        SubscriptionPeriodUnit.Month => new StorePeriod(period.Value, StorePeriodUnit.Month),
        SubscriptionPeriodUnit.Year => new StorePeriod(period.Value, StorePeriodUnit.Year),
        _ => null,
    };

    private static decimal Amount(Price? price) =>
        price?.AmountMicros is { } micros ? micros / 1_000_000m
        : price?.Amount is { } amount ? (decimal)amount
        : 0m;

    private static string BaseId(string id) => id.Split(':')[0];

    private static string PlanId(string id) => id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;

    private void Warn(string key, string message)
    {
        if (_warned.TryAdd(key, true))
            _logger.LogWarning("{Message}", message);
    }

    private static InvalidOperationException Failure(string what, RevenueCatError? error) =>
        new($"Spine purchases: RevenueCat could not get the {what}: {error?.Code} {error?.Message}".TrimEnd());
}
#endif
