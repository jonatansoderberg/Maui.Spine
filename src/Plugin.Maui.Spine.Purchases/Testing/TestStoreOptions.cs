namespace Plugin.Maui.Spine.Purchases.Testing;

/// <summary>Prices, periods and trials of the test store's products.</summary>
/// <typeparam name="TProduct">The app's product enum.</typeparam>
public sealed class TestStoreOptions<TProduct> where TProduct : struct, Enum
{
    internal TestStoreSettings Settings { get; } = new();

    /// <summary>The product's price. Without it, 10 in <see cref="Currency"/>.</summary>
    public TestStoreOptions<TProduct> Price(TProduct product, decimal amount)
    {
        Settings.For(product).Amount = amount;
        return this;
    }

    /// <summary>A subscription's period. Without it, a month.</summary>
    public TestStoreOptions<TProduct> Period(TProduct product, StorePeriod period)
    {
        Settings.For(product).Period = period;
        return this;
    }

    /// <summary>A free trial the first time the user buys the subscription.</summary>
    public TestStoreOptions<TProduct> Trial(TProduct product, StorePeriod trial)
    {
        Settings.For(product).Trial = trial;
        return this;
    }

    /// <summary>The ISO 4217 currency of every price. Without it, SEK.</summary>
    public string Currency { get => Settings.Currency; set => Settings.Currency = value; }

    /// <summary>Asks "Buy … for …?" before a purchase, as the store's own sheet does. On by default.</summary>
    public bool Confirm { get => Settings.Confirm; set => Settings.Confirm = value; }
}

internal sealed class TestStoreSettings
{
    private readonly Dictionary<Enum, TestProduct> _products = [];

    public string Currency { get; set; } = "SEK";
    public bool Confirm { get; set; } = true;

    public TestProduct For(Enum product) =>
        _products.TryGetValue(product, out var settings) ? settings : _products[product] = new TestProduct();
}

internal sealed class TestProduct
{
    public decimal Amount { get; set; } = 10m;
    public StorePeriod Period { get; set; } = StorePeriod.Month;
    public StorePeriod? Trial { get; set; }
}
