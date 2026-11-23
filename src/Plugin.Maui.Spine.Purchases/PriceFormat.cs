using System.Globalization;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>A price in a currency, written the way the user's own language writes that currency.</summary>
internal static class PriceFormat
{
    public static string Format(decimal amount, string currencyCode)
    {
        var culture = CultureInfo.CurrentCulture;
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();

        if (!IsCurrencyOf(culture, currencyCode))
        {
            // Another country's currency: its own symbol ("kr", "€", "$") in the user's number format.
            var home = CultureInfo.GetCultures(CultureTypes.SpecificCultures).FirstOrDefault(c => IsCurrencyOf(c, currencyCode));
            format.CurrencySymbol = home?.NumberFormat.CurrencySymbol ?? currencyCode;
        }

        format.CurrencyDecimalDigits = amount == decimal.Truncate(amount) ? 0 : 2;
        return amount.ToString("C", format);
    }

    private static bool IsCurrencyOf(CultureInfo culture, string currencyCode)
    {
        try
        {
            return !culture.IsNeutralCulture && culture.Name.Length > 0
                && new RegionInfo(culture.Name).ISOCurrencySymbol.Equals(currencyCode, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
