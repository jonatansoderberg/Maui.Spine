using System.Collections.Concurrent;
using System.ComponentModel;
using AsyncAwaitBestPractices;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// <see cref="IAppPurchases"/> over an <see cref="IPurchaseStore"/>: the last known owned products in <see cref="Preferences"/>,
/// each purchase worked out with <see cref="PurchaseResolver"/>, and <see cref="Changed"/> when one of them changes.
/// </summary>
internal sealed class AppPurchases : IAppPurchases
{
    private const string OwnedKey = "Spine.Purchases.Owned";
    private const string ProductsKey = "Spine.Purchases.Products";
    private const string OverlapKey = "Spine.Purchases.OverlapReported";

    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<(Enum, bool), PurchaseActiveSource> _sources = new();
    private List<OwnedProduct> _owned;
    private List<StoreProductInfo> _products;

    public AppPurchases(IServiceProvider services, PurchaseCatalog catalog, IPurchaseStore store, PurchasesSettings settings, TimeProvider time)
    {
        _services = services;
        _time = time;
        Catalog = catalog;
        Store = store;
        Settings = settings;
        _owned = PurchasesStorage.LoadOwned(OwnedKey, catalog);
        _products = PurchasesStorage.LoadProducts(ProductsKey, catalog);
        store.OwnedChanged += (_, _) => RefreshAsync().SafeFireAndForget(e => Log("refresh after a store event", e));
    }

    public PurchaseCatalog Catalog { get; }

    public IPurchaseStore Store { get; }

    public PurchasesSettings Settings { get; }

    /// <summary>The products from the last answer, for a sheet that opens before the store answers.</summary>
    public IReadOnlyList<StoreProductInfo> CachedProducts
    {
        get { lock (_gate) return [.. _products]; }
    }

    public IReadOnlyList<OwnedProduct> Owned
    {
        get { lock (_gate) return [.. _owned]; }
    }

    public event EventHandler<PurchasesChangedEventArgs>? Changed;

    public IReadOnlyList<OwnedProduct> OverlappingSubscriptions => PurchaseResolver.OverlappingSubscriptions(Catalog, Owned, _time.GetUtcNow());

    public bool IsActive<TPurchase>(TPurchase purchase) where TPurchase : struct, Enum => State(purchase).IsActive;

    public PurchaseState GetState<TPurchase>(TPurchase purchase) where TPurchase : struct, Enum => State(purchase);

    public PurchaseState State(Enum purchase)
    {
        var definition = Catalog.Purchase(purchase);
        return PurchaseResolver.Resolve(Catalog, Owned, _time.GetUtcNow())[definition.Value];
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        Apply(await Store.GetOwnedAsync(cancellationToken));

    public async Task<IReadOnlyList<StoreProductInfo>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var sellable = Catalog.Products.Where(p => p.Kind is ProductKind.Subscription or ProductKind.OneTime).ToList();
            var products = await Store.GetProductsAsync(sellable, cancellationToken);
            lock (_gate)
            {
                _products = [.. products];
                PurchasesStorage.SaveProducts(ProductsKey, _products);
            }
            return products;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log("products", e);
            return CachedProducts;
        }
    }

    public Task<PurchaseResult> PurchaseAsync<TProduct>(TProduct product, CancellationToken cancellationToken = default) where TProduct : struct, Enum =>
        Purchase(product, cancellationToken);

    public async Task<PurchaseResult> Purchase(Enum product, CancellationToken cancellationToken = default)
    {
        var definition = Catalog.Product(product);
        if (definition.Kind is ProductKind.Pass or ProductKind.Consumable)
            throw new NotSupportedException($"Spine purchases: {Catalog.ProductType.Name}.{product} is a {definition.Kind}, which Spine does not sell yet. P1 sells Subscription and OneTime products.");

        var now = _time.GetUtcNow();
        var owned = Owned;
        if (definition.Kind == ProductKind.Subscription && owned.Any(o => Equals(o.Product, product) && PurchaseResolver.IsActive(o, definition.Kind, now)))
            return PurchaseResult.Purchased;

        // Another subscription to the same purchase: the store changes the plan instead of selling a second one.
        var replacing = definition.Kind != ProductKind.Subscription ? null : owned.FirstOrDefault(o =>
            Catalog.Product(o.Product) is { Kind: ProductKind.Subscription } other
            && other.Grants.Intersect(definition.Grants).Any()
            && PurchaseResolver.IsActive(o, ProductKind.Subscription, now));

        PurchaseResult result;
        try
        {
            result = await Store.PurchaseAsync(definition, replacing, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log("purchase", e);
            return PurchaseResult.Failed(e.GetType().Name, e.Message);
        }

        if (result.Status == PurchaseStatus.Purchased)
            await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task<PurchaseResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Store.RestoreAsync(cancellationToken);
            await RefreshAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log("restore", e);
            return PurchaseResult.Failed(e.GetType().Name, e.Message);
        }

        var now = _time.GetUtcNow();
        return PurchaseResolver.Resolve(Catalog, Owned, now).Values.Any(s => s.IsActive) ? PurchaseResult.Purchased : PurchaseResult.NothingToRestore;
    }

    public Task ManageAsync(CancellationToken cancellationToken = default) => Store.ManageAsync(cancellationToken);

    public Task<bool> ShowPaywallAsync(CancellationToken cancellationToken = default) => OpenPaywallAsync(null, cancellationToken);

    public Task<bool> ShowPaywallAsync<TPurchase>(TPurchase purchase, CancellationToken cancellationToken = default) where TPurchase : struct, Enum =>
        OpenPaywallAsync(Catalog.Purchase(purchase).Value, cancellationToken);

    public async Task<bool> RequireAsync<TPurchase>(TPurchase purchase, CancellationToken cancellationToken = default) where TPurchase : struct, Enum =>
        IsActive(purchase) || await ShowPaywallAsync(purchase, cancellationToken);

    public async Task<bool> RequireAsync(Enum purchase, CancellationToken cancellationToken = default) =>
        State(purchase).IsActive || await OpenPaywallAsync(Catalog.Purchase(purchase).Value, cancellationToken);

    public async Task<bool> OpenPaywallAsync(Enum? purchase, CancellationToken cancellationToken)
    {
        var navigation = _services.GetRequiredService<INavigationService>();
        var request = new PaywallRequest(purchase);
        bool bought;

        if (Settings.PaywallPage is { } page)
        {
            // NavigateToWithResultAsync is generic in the page; the app's own page is only known as a Type here.
            var open = typeof(INavigationService).GetMethods()
                .Single(m => m.Name == nameof(INavigationService.NavigateToWithResultAsync) && m.GetGenericArguments().Length == 3)
                .MakeGenericMethod(page, typeof(PaywallRequest), typeof(bool));
            var outcome = await (Task<NavigationResult<bool>>)open.Invoke(navigation, [request])!;
            bought = outcome is { IsSuccess: true, Value: true };
        }
        else
        {
            var outcome = await navigation.NavigateToWithResultAsync<PaywallSheet, PaywallRequest, bool>(request);
            bought = outcome is { IsSuccess: true, Value: true };
        }

        return purchase is null ? bought : State(purchase).IsActive;
    }

    /// <summary>The products the paywall shows for <paramref name="purchase"/>, in the offered order, of those the store sells.</summary>
    public IReadOnlyList<StoreProductInfo> PaywallProducts(Enum? purchase, IReadOnlyList<StoreProductInfo> sellable)
    {
        var now = _time.GetUtcNow();
        var owned = Owned;
        var lifetime = owned
            .Where(o => Catalog.Product(o.Product).Kind == ProductKind.OneTime)
            .SelectMany(o => Catalog.Product(o.Product).Grants)
            .ToHashSet();

        var order = Settings.Paywall.Offer ?? [.. Catalog.Products.Select(p => p.Value)];
        var wanted = purchase is null ? null : Catalog.Purchase(purchase);

        return [.. order
            .Select(value => sellable.FirstOrDefault(s => Equals(s.Product, value)))
            .OfType<StoreProductInfo>()
            .Where(s =>
            {
                var definition = Catalog.Product(s.Product);
                if (wanted is not null && !definition.Grants.Contains(wanted))
                    return false;
                // A subscription to what a one-time purchase already gives would charge for nothing.
                return definition.Kind != ProductKind.Subscription || !definition.Grants.All(lifetime.Contains);
            })];
    }

    /// <summary>One shared, observable "is it active" per purchase, for <see cref="PurchaseActiveExtension"/>.</summary>
    public PurchaseActiveSource Source(Enum purchase, bool invert)
    {
        var definition = Catalog.Purchase(purchase);
        return _sources.GetOrAdd((definition.Value, invert), key => new PurchaseActiveSource(this, key.Item1, key.Item2));
    }

    private void Apply(IReadOnlyList<OwnedProduct> owned)
    {
        var now = _time.GetUtcNow();
        List<Enum> changed;
        bool overlap;

        lock (_gate)
        {
            var before = PurchaseResolver.Resolve(Catalog, _owned, now);
            _owned = [.. owned];
            PurchasesStorage.SaveOwned(OwnedKey, _owned);
            var after = PurchaseResolver.Resolve(Catalog, _owned, now);
            changed = [.. after.Where(kv => !Equals(before[kv.Key], kv.Value)).Select(kv => kv.Key)];

            var overlapping = PurchaseResolver.OverlappingSubscriptions(Catalog, _owned, now).Select(o => o.Product.ToString()).ToList();
            var reported = PurchasesStorage.LoadNames(OverlapKey);
            overlap = overlapping.Except(reported).Any();
            if (!overlapping.SequenceEqual(reported))
                PurchasesStorage.SaveNames(OverlapKey, overlapping);
        }

        if (changed.Count == 0 && !overlap)
            return;

        var args = new PurchasesChangedEventArgs(changed, overlap);
        if (MainThread.IsMainThread)
            Raise(args);
        else
            MainThread.BeginInvokeOnMainThread(() => Raise(args));
    }

    private void Raise(PurchasesChangedEventArgs args)
    {
        foreach (var source in _sources.Values)
            source.Update();
        Changed?.Invoke(this, args);
    }

    private static void Log(string what, Exception e) =>
        System.Diagnostics.Debug.WriteLine($"[Spine] Purchases: {what} failed: {e}");
}

/// <summary>Whether one purchase is active, as a bindable value that follows <see cref="IAppPurchases.Changed"/>.</summary>
internal sealed class PurchaseActiveSource(AppPurchases purchases, Enum purchase, bool invert) : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs ValueChanged = new(nameof(Value));

    public bool Value => purchases.State(purchase).IsActive != invert;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update() => PropertyChanged?.Invoke(this, ValueChanged);
}
