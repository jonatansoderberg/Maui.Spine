using System.Text.RegularExpressions;

namespace Plugin.Maui.Spine.Purchases.Testing;

/// <summary>A store on the device, for the Showcase, tests and development: purchases in <see cref="Preferences"/>, no account.</summary>
internal sealed partial class TestStore(PurchaseCatalog catalog, TestStoreSettings settings, TimeProvider time) : IPurchaseStore, ITestStore
{
    private const string OwnedKey = "Spine.Purchases.TestStore.Owned";
    private const string PendingKey = "Spine.Purchases.TestStore.Pending";
    private const string TrialsKey = "Spine.Purchases.TestStore.Trials";

    private readonly Lock _gate = new();
    private List<OwnedProduct> _owned = PurchasesStorage.LoadOwned(OwnedKey, catalog);
    private List<string> _pending = PurchasesStorage.LoadNames(PendingKey);
    private List<string> _trials = PurchasesStorage.LoadNames(TrialsKey);

    public string Name => PurchasesStrings.Get("Spine.Purchases.Store.Test");

    public event EventHandler? OwnedChanged;

    public IReadOnlyList<OwnedProduct> Owned
    {
        get { lock (_gate) return [.. _owned]; }
    }

    public IReadOnlyList<Enum> PendingPurchases
    {
        get { lock (_gate) return [.. _pending.Select(catalog.ProductNamed).OfType<ProductDefinition>().Select(p => p.Value)]; }
    }

    public bool NextPurchasePending { get; set; }

    public bool NextPurchaseFails { get; set; }

    public Task<IReadOnlyList<OwnedProduct>> GetOwnedAsync(CancellationToken cancellationToken) => Task.FromResult(Owned);

    public Task<IReadOnlyList<StoreProductInfo>> GetProductsAsync(IReadOnlyList<ProductDefinition> products, CancellationToken cancellationToken)
    {
        IReadOnlyList<StoreProductInfo> list = [.. products
            .Where(p => p.Kind is ProductKind.Subscription or ProductKind.OneTime)
            .Select(p =>
            {
                var test = settings.For(p.Value);
                var subscription = p.Kind == ProductKind.Subscription;
                bool trialUsed;
                lock (_gate) trialUsed = _trials.Contains(p.Value.ToString());
                return new StoreProductInfo(p.Value, p.Kind, Humanize(p.Value.ToString()), PriceFormat.Format(test.Amount, settings.Currency),
                    test.Amount, settings.Currency, subscription ? test.Period : null, subscription && !trialUsed ? test.Trial : null);
            })];
        return Task.FromResult(list);
    }

    public async Task<PurchaseResult> PurchaseAsync(ProductDefinition product, OwnedProduct? replacing, CancellationToken cancellationToken)
    {
        if (product.Kind == ProductKind.OneTime && Owned.Any(o => Equals(o.Product, product.Value)))
            return PurchaseResult.Purchased;

        if (settings.Confirm && !await ConfirmAsync(product))
            return PurchaseResult.Cancelled;

        if (NextPurchaseFails)
        {
            NextPurchaseFails = false;
            return PurchaseResult.Failed("test", PurchasesStrings.Get("Spine.Purchases.TestStore.Failed"));
        }

        if (NextPurchasePending)
        {
            NextPurchasePending = false;
            lock (_gate)
            {
                _pending.Add(product.Value.ToString());
                PurchasesStorage.SaveNames(PendingKey, _pending);
            }
            return PurchaseResult.Pending;
        }

        Update(owned =>
        {
            if (replacing is not null)
                owned.RemoveAll(o => Equals(o.Product, replacing.Product));
            owned.Add(Bought(product));
        });
        return PurchaseResult.Purchased;
    }

    public Task RestoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ManageAsync(CancellationToken cancellationToken) =>
        Application.Current?.Windows.FirstOrDefault()?.Page?.DisplayAlertAsync(
            Name, PurchasesStrings.Get("Spine.Purchases.TestStore.Manage"), PurchasesStrings.Get("Spine.Purchases.Ok"))
        ?? Task.CompletedTask;

    public void Expire(Enum product) => Change(product, o => o with { ExpiresAt = time.GetUtcNow().AddSeconds(-1), WillRenew = false, Phase = PurchasePhase.Regular });

    public void Renew(Enum product)
    {
        var period = settings.For(product).Period;
        Change(product, o =>
        {
            var now = time.GetUtcNow();
            var from = o.ExpiresAt is { } end && end > now ? end : now;
            return o with { ExpiresAt = period.After(from), WillRenew = true, Phase = PurchasePhase.Regular };
        });
    }

    public void Cancel(Enum product) => Change(product, o => o with { WillRenew = false });

    public void Refund(Enum product) => Update(owned => owned.RemoveAll(o => Equals(o.Product, product)));

    public void StartGracePeriod(Enum product) =>
        Change(product, o => o with { ExpiresAt = time.GetUtcNow().AddMinutes(-1), WillRenew = true, Phase = PurchasePhase.GracePeriod });

    public void ApprovePending()
    {
        List<string> pending;
        lock (_gate)
        {
            pending = [.. _pending];
            _pending.Clear();
            PurchasesStorage.SaveNames(PendingKey, _pending);
        }

        Update(owned =>
        {
            foreach (var product in pending.Select(catalog.ProductNamed).OfType<ProductDefinition>())
            {
                if (product.Kind == ProductKind.Subscription)
                    owned.RemoveAll(o => catalog.Product(o.Product).Kind == ProductKind.Subscription && catalog.Product(o.Product).Grants.Intersect(product.Grants).Any());
                if (!owned.Any(o => Equals(o.Product, product.Value)))
                    owned.Add(Bought(product));
            }
        });
    }

    public void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
            _trials.Clear();
            PurchasesStorage.SaveNames(PendingKey, _pending);
            PurchasesStorage.SaveNames(TrialsKey, _trials);
        }
        NextPurchasePending = false;
        NextPurchaseFails = false;
        Update(owned => owned.Clear());
    }

    private OwnedProduct Bought(ProductDefinition product)
    {
        var now = time.GetUtcNow();
        if (product.Kind != ProductKind.Subscription)
            return new OwnedProduct(product.Value, now, null, false);

        var test = settings.For(product.Value);
        lock (_gate)
        {
            if (test.Trial is { } trial && !_trials.Contains(product.Value.ToString()))
            {
                _trials.Add(product.Value.ToString());
                PurchasesStorage.SaveNames(TrialsKey, _trials);
                return new OwnedProduct(product.Value, now, trial.After(now), true, PurchasePhase.Trial);
            }
        }

        return new OwnedProduct(product.Value, now, test.Period.After(now), true);
    }

    private void Change(Enum product, Func<OwnedProduct, OwnedProduct> change) =>
        Update(owned =>
        {
            var index = owned.FindIndex(o => Equals(o.Product, product));
            if (index >= 0)
                owned[index] = change(owned[index]);
        });

    private void Update(Action<List<OwnedProduct>> change)
    {
        lock (_gate)
        {
            var owned = new List<OwnedProduct>(_owned);
            change(owned);
            _owned = owned;
            PurchasesStorage.SaveOwned(OwnedKey, owned);
        }
        OwnedChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> ConfirmAsync(ProductDefinition product)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return true;

        var test = settings.For(product.Value);
        var price = PriceFormat.Format(test.Amount, settings.Currency);
        if (product.Kind == ProductKind.Subscription)
            price = PurchasesStrings.PerPeriod(price, test.Period);

        return await MainThread.InvokeOnMainThreadAsync(() => page.DisplayAlertAsync(
            Name,
            PurchasesStrings.Get("Spine.Purchases.TestStore.Confirm", PurchasesStrings.ProductName(product.Value), price),
            PurchasesStrings.Get("Spine.Purchases.TestStore.Buy"),
            PurchasesStrings.Get("Spine.Purchases.Cancel")));
    }

    private static string Humanize(string name) => CamelBoundary().Replace(name, " $1");

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex CamelBoundary();
}
