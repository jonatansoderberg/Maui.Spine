using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>Owned products and product lists in <see cref="Preferences"/>, by the product's member name.</summary>
internal static class PurchasesStorage
{
    public static List<OwnedProduct> LoadOwned(string key, PurchaseCatalog catalog) =>
        [.. Read(key, PurchasesJson.Default.ListOwnedRecord)
            .Select(r => catalog.ProductNamed(r.Product) is { } product
                ? new OwnedProduct(product.Value, r.PurchasedAt, r.ExpiresAt, r.WillRenew, r.Phase)
                : null)
            .OfType<OwnedProduct>()];

    public static void SaveOwned(string key, IEnumerable<OwnedProduct> owned) =>
        Write(key, owned.Select(o => new OwnedRecord(o.Product.ToString(), o.PurchasedAt, o.ExpiresAt, o.WillRenew, o.Phase)).ToList(), PurchasesJson.Default.ListOwnedRecord);

    public static List<StoreProductInfo> LoadProducts(string key, PurchaseCatalog catalog) =>
        [.. Read(key, PurchasesJson.Default.ListProductRecord)
            .Select(r => catalog.ProductNamed(r.Product) is { } product
                ? new StoreProductInfo(product.Value, r.Kind, r.Title, r.Price, r.Amount, r.CurrencyCode, Period(r.Period), Period(r.FreeTrial))
                : null)
            .OfType<StoreProductInfo>()];

    public static void SaveProducts(string key, IEnumerable<StoreProductInfo> products) =>
        Write(key, products.Select(p => new ProductRecord(p.Product.ToString(), p.Kind, p.Title, p.Price, p.Amount, p.CurrencyCode, Record(p.Period), Record(p.FreeTrial))).ToList(), PurchasesJson.Default.ListProductRecord);

    public static List<string> LoadNames(string key) => Read(key, PurchasesJson.Default.ListString);

    public static void SaveNames(string key, IEnumerable<string> names) => Write(key, names.ToList(), PurchasesJson.Default.ListString);

    private static List<T> Read<T>(string key, System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> type)
    {
        try
        {
            return Preferences.Get(key, null) is { Length: > 0 } json ? JsonSerializer.Deserialize(json, type) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void Write<T>(string key, List<T> value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> type) =>
        Preferences.Set(key, JsonSerializer.Serialize(value, type));

    private static StorePeriod? Period(PeriodRecord? r) => r is null ? null : new StorePeriod(r.Count, r.Unit);

    private static PeriodRecord? Record(StorePeriod? p) => p is null ? null : new PeriodRecord(p.Count, p.Unit);
}

internal sealed record OwnedRecord(string Product, DateTimeOffset PurchasedAt, DateTimeOffset? ExpiresAt, bool WillRenew, PurchasePhase Phase);

internal sealed record PeriodRecord(int Count, StorePeriodUnit Unit);

internal sealed record ProductRecord(string Product, ProductKind Kind, string Title, string Price, decimal Amount, string CurrencyCode, PeriodRecord? Period, PeriodRecord? FreeTrial);

[JsonSerializable(typeof(List<OwnedRecord>))]
[JsonSerializable(typeof(List<ProductRecord>))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
internal sealed partial class PurchasesJson : JsonSerializerContext;
