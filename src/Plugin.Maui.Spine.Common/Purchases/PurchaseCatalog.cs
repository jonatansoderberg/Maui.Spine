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
    /// <summary>The credits a consumable product adds (<see cref="AddsAttribute"/>).</summary>
    public IReadOnlyList<CreditAmount> Adds { get; init; } = [];

    /// <summary>The product's id in the store of <paramref name="platform"/>, or null when it is not sold there.</summary>
    public string? IdFor(StorePlatform platform) => platform switch
    {
        StorePlatform.Apple => Apple,
        StorePlatform.Google => Google,
        _ => Microsoft,
    };
}

/// <summary>One member of the app's credit enum.</summary>
/// <param name="Value">The member.</param>
/// <param name="Id">Its id on the wire and in the ledger: the member's name.</param>
/// <param name="Free">Credits every caller gets once (<see cref="FreeCreditsAttribute"/>).</param>
/// <param name="UnlimitedWith">Purchases that make spending it free (<see cref="UnlimitedWithAttribute"/>).</param>
public sealed record CreditDefinition(Enum Value, string Id, int Free, IReadOnlyList<PurchaseDefinition> UnlimitedWith);

/// <summary>An amount of a credit.</summary>
/// <param name="Credit">The credit.</param>
/// <param name="Amount">How many.</param>
public sealed record CreditAmount(CreditDefinition Credit, int Amount);

/// <summary>
/// The app's two enums read once: every purchase with its id, and every product with its kind, store ids and what it
/// grants. Shared by the app and its server, so both read the same attributes the same way.
/// </summary>
public sealed class PurchaseCatalog
{
    private readonly Dictionary<Enum, PurchaseDefinition> _purchases;
    private readonly Dictionary<Enum, ProductDefinition> _products;
    private readonly Dictionary<Enum, CreditDefinition> _credits;

    private PurchaseCatalog(Type purchaseType, Type productType, Type? creditType, Dictionary<Enum, PurchaseDefinition> purchases,
        Dictionary<Enum, ProductDefinition> products, Dictionary<Enum, CreditDefinition> credits)
    {
        PurchaseType = purchaseType;
        ProductType = productType;
        CreditType = creditType;
        _purchases = purchases;
        _products = products;
        _credits = credits;
    }

    /// <summary>The app's credit enum, or null when it sells no credits.</summary>
    public Type? CreditType { get; }

    /// <summary>Every credit, in declaration order.</summary>
    public IEnumerable<CreditDefinition> Credits => _credits.Values;

    /// <summary>The app's purchase enum.</summary>
    public Type PurchaseType { get; }

    /// <summary>The app's product enum.</summary>
    public Type ProductType { get; }

    /// <summary>Every purchase, in declaration order.</summary>
    public IEnumerable<PurchaseDefinition> Purchases => _purchases.Values;

    /// <summary>Every product, in declaration order.</summary>
    public IEnumerable<ProductDefinition> Products => _products.Values;

    /// <summary>Reads <typeparamref name="TPurchase"/> and <typeparamref name="TProduct"/>, for an app that sells no credits.</summary>
    /// <exception cref="InvalidOperationException">A product grants something that is not a <typeparamref name="TPurchase"/>, has no <see cref="StoreProductAttribute"/>, or adds credits.</exception>
    public static PurchaseCatalog Create<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TPurchase,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TProduct>()
        where TPurchase : struct, Enum
        where TProduct : struct, Enum =>
        Read(typeof(TPurchase), typeof(TProduct), null);

    /// <summary>Reads <typeparamref name="TPurchase"/>, <typeparamref name="TProduct"/> and the credits in <typeparamref name="TCredit"/>.</summary>
    /// <exception cref="InvalidOperationException">An attribute names a member of the wrong enum, a product has no <see cref="StoreProductAttribute"/>, or a consumable adds nothing.</exception>
    public static PurchaseCatalog Create<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TPurchase,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TProduct,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TCredit>()
        where TPurchase : struct, Enum
        where TProduct : struct, Enum
        where TCredit : struct, Enum =>
        Read(typeof(TPurchase), typeof(TProduct), typeof(TCredit));

    private static PurchaseCatalog Read(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type purchaseType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type productType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type? creditType)
    {
        var purchases = new Dictionary<Enum, PurchaseDefinition>();
        foreach (var field in purchaseType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (Enum)field.GetValue(null)!;
            purchases[value] = new PurchaseDefinition(value, field.GetCustomAttribute<PurchaseIdAttribute>()?.Id ?? field.Name);
        }

        var credits = new Dictionary<Enum, CreditDefinition>();
        foreach (var field in creditType?.GetFields(BindingFlags.Public | BindingFlags.Static) ?? [])
        {
            var value = (Enum)field.GetValue(null)!;
            var unlimited = new List<PurchaseDefinition>();
            foreach (var purchase in field.GetCustomAttribute<UnlimitedWithAttribute>()?.Purchases ?? [])
            {
                if (purchase is not Enum e || e.GetType() != purchaseType)
                    throw new InvalidOperationException($"Spine purchases: {creditType!.Name}.{field.Name} is unlimited with {purchase}, which is not a {purchaseType.Name}.");
                unlimited.Add(purchases[e]);
            }
            credits[value] = new CreditDefinition(value, field.Name, field.GetCustomAttribute<FreeCreditsAttribute>()?.Amount ?? 0, unlimited);
        }

        var products = new Dictionary<Enum, ProductDefinition>();
        foreach (var field in productType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (Enum)field.GetValue(null)!;
            var name = $"{productType.Name}.{field.Name}";
            var store = field.GetCustomAttribute<StoreProductAttribute>()
                ?? throw new InvalidOperationException($"Spine purchases: {name} has no [StoreProduct]. Every product says what kind it is: [StoreProduct(ProductKind.Subscription, Apple = …)].");

            var grants = new List<PurchaseDefinition>();
            foreach (var granted in field.GetCustomAttribute<GrantsAttribute>()?.Purchases ?? [])
            {
                if (granted is not Enum e || e.GetType() != purchaseType)
                    throw new InvalidOperationException($"Spine purchases: {name} grants {granted} ({granted?.GetType().Name ?? "null"}), which is not a {purchaseType.Name}.");
                grants.Add(purchases[e]);
            }

            var adds = new List<CreditAmount>();
            foreach (var add in field.GetCustomAttributes<AddsAttribute>())
            {
                if (creditType is null)
                    throw new InvalidOperationException($"Spine purchases: {name} adds {add.Credit}, but no credit enum is registered. Register it with o.Credits<TCredit>(…).");
                if (add.Credit is not Enum e || e.GetType() != creditType)
                    throw new InvalidOperationException($"Spine purchases: {name} adds {add.Credit}, which is not a {creditType.Name}.");
                if (add.Amount <= 0)
                    throw new InvalidOperationException($"Spine purchases: {name} adds {add.Amount} {add.Credit}; a product adds at least one.");
                adds.Add(new CreditAmount(credits[e], add.Amount));
            }

            if (store.Kind == ProductKind.Consumable && adds.Count == 0)
                throw new InvalidOperationException($"Spine purchases: {name} is a Consumable but adds nothing. Say what it adds: [Adds(AppCredit.…, 1)].");
            if (store.Kind != ProductKind.Consumable && adds.Count > 0)
                throw new InvalidOperationException($"Spine purchases: {name} adds credits but is a {store.Kind}. A product that adds credits is a ProductKind.Consumable, bought again and again.");

            products[value] = new ProductDefinition(value, store.Kind, store.Apple, store.Google, store.Microsoft, grants) { Adds = adds };
        }

        return new PurchaseCatalog(purchaseType, productType, creditType, purchases, products, credits);
    }

    /// <summary>The credit for <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a member of the registered credit enum.</exception>
    public CreditDefinition Credit(object? value) =>
        value is Enum e && _credits.TryGetValue(e, out var definition) ? definition
            : throw new ArgumentException(CreditType is null
                ? $"Spine purchases: {Describe(value)} is a credit, but no credit enum is registered. Register it with o.Credits<TCredit>(…)."
                : $"Spine purchases: {Describe(value)} is not a member of the registered credit enum {CreditType.Name}.", nameof(value));

    /// <summary>The credit whose id is <paramref name="id"/>, or null.</summary>
    public CreditDefinition? CreditNamed(string id) => _credits.Values.FirstOrDefault(c => c.Id == id);

    /// <summary>The products that add <paramref name="credit"/>.</summary>
    public IEnumerable<ProductDefinition> ProductsAdding(CreditDefinition credit) =>
        _products.Values.Where(p => p.Adds.Any(a => a.Credit == credit));

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
