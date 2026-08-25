using System.Globalization;
using System.Text.Json;

namespace Orientera.Services.Weather;

/// <summary>
/// MET Norways punktprognos (Locationforecast 2.0), läst till det den här appen frågar efter:
/// vad är det för väder nu.
/// </summary>
/// <remarks>
/// Ren, och därför testbar. Nätverket och positionen bor i <see cref="WeatherService"/>; det här
/// är bara svaret.
/// <para>
/// MET och inte SMHI, som beslut D13 först pekade ut: SMHI:s
/// <c>opendata-download-metfcst.smhi.se</c> svarar 404 på hela värden, API-roten inräknad —
/// tjänsten finns inte längre på den adressen. MET:s är fri och nyckellös på samma sätt, men
/// kräver två saker till: en User-Agent som säger vem som frågar, och en kreditering av källan.
/// Båda finns; se <see cref="WeatherService"/> respektive Jag-sidans källrad.
/// </para>
/// </remarks>
public static class MetForecast
{
    /// <summary>
    /// Den timme som ligger närmast <paramref name="now"/>. Prognosen börjar vid närmaste hela
    /// timme, så "nu" är antingen den eller timmen som just passerat — och skillnaden mellan dem
    /// är en halv grad, inte ett väderomslag.
    /// </summary>
    public static CurrentWeather? Parse(string json, DateTimeOffset now, string place)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("properties", out var properties)
                || !properties.TryGetProperty("timeseries", out var series)
                || series.ValueKind is not JsonValueKind.Array)
                return null;

            JsonElement? nearest = null;
            var distance = TimeSpan.MaxValue;

            foreach (var entry in series.EnumerateArray())
            {
                if (!entry.TryGetProperty("time", out var time)
                    || !DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out var at))
                    continue;

                var away = (at - now).Duration();

                if (away >= distance)
                    continue;

                distance = away;
                nearest = entry;
            }

            if (nearest is not { } hour
                || !hour.TryGetProperty("data", out var data)
                || Temperature(data) is not { } temperature
                || Symbol(data) is not { } symbol)
                return null;

            return new CurrentWeather(temperature, symbol, place, now);
        }
        catch (JsonException)
        {
            // Ett svar appen inte förstår är samma sak som inget svar: raden uteblir.
            return null;
        }
    }

    private static double? Temperature(JsonElement data) =>
        data.TryGetProperty("instant", out var instant)
        && instant.TryGetProperty("details", out var details)
        && details.TryGetProperty("air_temperature", out var temperature)
            ? temperature.GetDouble()
            : null;

    /// <summary>
    /// Symbolen beskriver en period och inte ett ögonblick, så den ligger under nästa timme —
    /// och under nästa sex när prognosen kommit så långt fram att den slutat räkna per timme.
    /// </summary>
    private static string? Symbol(JsonElement data)
    {
        foreach (var window in (ReadOnlySpan<string>)["next_1_hours", "next_6_hours", "next_12_hours"])
        {
            if (data.TryGetProperty(window, out var period)
                && period.TryGetProperty("summary", out var summary)
                && summary.TryGetProperty("symbol_code", out var code)
                && code.GetString() is { Length: > 0 } symbol)
                return symbol;
        }

        return null;
    }
}
