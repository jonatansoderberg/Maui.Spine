using System.Text.RegularExpressions;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>The package's text (keys <c>Spine.Purchases.*</c>), and the app's names for its purchases and products.</summary>
internal static partial class PurchasesStrings
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(PurchasesStrings).Assembly));
    }

    public static string Get(string key)
    {
        EnsureRegistered();
        return SpineStrings.Current[key];
    }

    public static string Get(string key, params object?[] args)
    {
        EnsureRegistered();
        return SpineStrings.Current.Get(key, args);
    }

    /// <summary>Text as given: the Spine string with that key, or the text itself.</summary>
    public static string Text(string textOrKey) =>
        SpineStrings.Current.Has(textOrKey) ? SpineStrings.Current[textOrKey] : textOrKey;

    /// <summary>The app's name for a purchase: <c>Purchases.&lt;Member&gt;</c> in its strings, or the member's name.</summary>
    public static string PurchaseName(Enum purchase) => Named("Purchases." + purchase, purchase);

    /// <summary>The app's name for a product: <c>Purchases.Products.&lt;Member&gt;</c> in its strings, or the member's name.</summary>
    public static string ProductName(Enum product) => Named("Purchases.Products." + product, product);

    /// <summary>The app's name for a credit: <c>Purchases.Credits.&lt;Member&gt;</c> in its strings, or the member's name.</summary>
    public static string CreditName(Enum credit) => Named("Purchases.Credits." + credit, credit);

    /// <summary>The first letter in capitals, for a name that starts a row.</summary>
    public static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpper(text[0], SpineStrings.Current.Culture) + text[1..];

    /// <summary>
    /// The credit's row in the settings section: <c>Purchases.Credits.&lt;Member&gt;.Row</c> in the app's strings ("Livematcher"),
    /// or the credit's name with a capital.
    /// </summary>
    public static string CreditRowName(Enum credit)
    {
        EnsureRegistered();
        var key = $"Purchases.Credits.{credit}.Row";
        return SpineStrings.Current.Has(key) ? SpineStrings.Current[key] : Capitalized(CreditName(credit));
    }

    /// <summary>"7 days", "1 month".</summary>
    public static string Period(StorePeriod period)
    {
        EnsureRegistered();
        return SpineStrings.Current.Get("Spine.Purchases.Period." + period.Unit, period.Count);
    }

    /// <summary>"99 kr/year", or "99 kr every 3 months".</summary>
    public static string PerPeriod(string price, StorePeriod period) =>
        period.Count == 1
            ? Get("Spine.Purchases.Per." + period.Unit, price)
            : Get("Spine.Purchases.Every", price, Period(period));

    public static string Date(DateTimeOffset date) =>
        date.ToLocalTime().ToString("d MMM yyyy", SpineStrings.Current.Culture);

    private static string Named(string key, Enum value)
    {
        EnsureRegistered();
        return SpineStrings.Current.Has(key) ? SpineStrings.Current[key] : CamelBoundary().Replace(value.ToString(), " $1");
    }

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex CamelBoundary();
}
