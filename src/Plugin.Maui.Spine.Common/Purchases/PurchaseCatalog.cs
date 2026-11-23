using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>The store platforms a product can have an id for.</summary>
public enum StorePlatform
{
    /// <summary>App Store: iOS and Mac Catalyst.</summary>
    Apple,

    /// <summary>Google Play.</summary>
    Google,

    /// <summary>Microsoft Store.</summary>
    Microsoft,
}

/// <summary>One member of the app's purchase enum.</summary>
/// <param name="Value">The member.</param>
/// <param name="Id">Its id in the store service (<see cref="PurchaseIdAttribute"/>, or the member's name).</param>
public sealed record PurchaseDefinition(Enum Value, string Id);

/// <summary>One member of the app's product enum.</summary>
/// <param name="Value">The member.</param>
/// <param name="Kind">What kind of product it is.</param>
/// <param name="Apple">The App Store product id.</param>
/// <param name="Google">The Play product id.</param>
/// <param name="Microsoft">The Microsoft Store ID.</param>
/// <param name="Grants">The purchases it gives.</param>
public sealed record ProductDefinition(Enum Value, ProductKind Kind, string? Apple, string? Google, string? Microsoft, IReadOnlyList<PurchaseDefinition> Grants)
{
    /// <summary>The product's id in the store of <paramref name="platform"/>, or null when it is not sold there.</summary>
    public string? IdFor(StorePlatform platform) => platform switch
    {
        StorePlatform.Apple => Apple,
        StorePlatform.Google => Google,
        _ => Microsoft,
    };
}

/// <summary>
/// The app's two enums read once: every purchase with its id, and every product with its kind, store ids and what it
/// grants. Shared by the app and its server, so both read the same attributes the same way.
/// </summary>
public sealed class PurchaseCatalog
{
    private readonly Dictionary<Enum, PurchaseDefinition> _purchases;
    private readonly Dictionary<Enum, ProductDefinition> _products;

    private PurchaseCatalog(Type purchaseType, Type productType, Dictionary<Enum, PurchaseDefinition> purchases, Dictionary<Enum, ProductDefinition> products)
    {
        PurchaseType = purchaseType;
        ProductType = productType;
        _purchases = purchases;
        _products = products;
    }

    /// <summary>The app's purchase enum.</summary>
    public Type PurchaseType { get; }

    /// <summary>The app's product enum.</summary>
    public Type ProductType { get; }

    /// <summary>Every purchase, in declaration order.</summary>
    public IEnumerable<PurchaseDefinition> Purchases => _purchases.Values;

    /// <summary>Every product, in declaration order.</summary>
    public IEnumerable<ProductDefinition> Products => _products.Values;

    /// <summary>Reads <typeparamref name="TPurchase"/> and <typeparamref name="TProduct"/>.</summary>
    /// <exception cref="InvalidOperationException">A product grants something that is not a <typeparamref name="TPurchase"/>, or has no <see cref="StoreProductAttribute"/>.</exception>
    public static PurchaseCatalog Create<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TPurchase,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TProduct>()
        where TPurchase : struct, Enum
        where TProduct : struct, Enum
    {
        var purchases = new Dictionary<Enum, PurchaseDefinition>();
        foreach (var field in typeof(TPurchase).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (Enum)field.GetValue(null)!;
            purchases[value] = new PurchaseDefinition(value, field.GetCustomAttribute<PurchaseIdAttribute>()?.Id ?? field.Name);
        }

        var products = new Dictionary<Enum, ProductDefinition>();
        foreach (var field in typeof(TProduct).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (Enum)field.GetValue(null)!;
            var store = field.GetCustomAttribute<StoreProductAttribute>()
                ?? throw new InvalidOperationException($"Spine purchases: {typeof(TProduct).Name}.{field.Name} has no [StoreProduct]. Every product says what kind it is: [StoreProduct(ProductKind.Subscription, Apple = …)].");

            var grants = new List<PurchaseDefinition>();
            foreach (var granted in field.GetCustomAttribute<GrantsAttribute>()?.Purchases ?? [])
            {
                if (granted is not TPurchase purchase)
                    throw new InvalidOperationException($"Spine purchases: {typeof(TProduct).Name}.{field.Name} grants {granted} ({granted?.GetType().Name ?? "null"}), which is not a {typeof(TPurchase).Name}.");
                grants.Add(purchases[purchase]);
            }

            products[value] = new ProductDefinition(value, store.Kind, store.Apple, store.Google, store.Microsoft, grants);
        }

        return new PurchaseCatalog(typeof(TPurchase), typeof(TProduct), purchases, products);
    }

    /// <summary>The purchase for <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a member of the registered purchase enum.</exception>
    public PurchaseDefinition Purchase(object? value) =>
        value is Enum e && _purchases.TryGetValue(e, out var definition) ? definition
            : throw new ArgumentException($"Spine purchases: {Describe(value)} is not a member of the registered purchase enum {PurchaseType.Name} (products: {ProductType.Name}).", nameof(value));

    /// <summary>The product for <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a member of the registered product enum.</exception>
    public ProductDefinition Product(object? value) =>
        value is Enum e && _products.TryGetValue(e, out var definition) ? definition
            : throw new ArgumentException($"Spine purchases: {Describe(value)} is not a member of the registered product enum {ProductType.Name} (purchases: {PurchaseType.Name}).", nameof(value));

    /// <summary>The product whose id in <paramref name="platform"/>'s store is <paramref name="storeId"/>, or null.</summary>
    public ProductDefinition? ProductForStoreId(StorePlatform platform, string storeId) =>
        _products.Values.FirstOrDefault(p => string.Equals(p.IdFor(platform), storeId, StringComparison.Ordinal));

    /// <summary>The product whose member name is <paramref name="name"/>, or null.</summary>
    public ProductDefinition? ProductNamed(string name) =>
        _products.Values.FirstOrDefault(p => p.Value.ToString() == name);

    /// <summary>The products that grant <paramref name="purchase"/>.</summary>
    public IEnumerable<ProductDefinition> ProductsGranting(PurchaseDefinition purchase) =>
        _products.Values.Where(p => p.Grants.Contains(purchase));

    private static string Describe(object? value) => value is null ? "null" : $"{value.GetType().Name}.{value}";
}
