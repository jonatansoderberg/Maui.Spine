namespace Orientera.Services.Weather;

/// <summary>
/// Vädret där läsaren är, just nu.
/// </summary>
/// <param name="TemperatureC">Grader Celsius, oavrundade — avrundningen är presentationens sak.</param>
/// <param name="Symbol">MET Norways <c>symbol_code</c>: "clearsky_day", "lightrain", "fog".</param>
/// <param name="Place">Orten, som en människa säger den: "Gävle".</param>
/// <param name="ReadAt">När svaret hämtades. Det är den som avgör om det fortfarande är väder.</param>
public sealed record CurrentWeather(double TemperatureC, string Symbol, string Place, DateTimeOffset ReadAt);

/// <summary>
/// Hur en vädersymbol sägs — i tecken och i ord.
/// </summary>
/// <remarks>
/// Två former för samma sak, av samma skäl som <c>Format.SpokenTime</c> finns: tecknet bär
/// meningen för den som ser det, och ordet för den som får raden uppläst. Aldrig bara tecknet.
/// <para>
/// Emoji och inte en ritad väg, till skillnad från disciplinmärkena. De märkena skiljer sig åt i
/// grad och behöver temats färg för att gå isär; ett väder skiljer sig i art, och sol mot regn
/// syns på formen. Samma val som medaljerna i <c>Format.Medal</c>.
/// </para>
/// <para>
/// Koderna läses på delsträng och inte som en sluten mängd. MET har ett fyrtiotal av dem, byggda
/// av samma ord i olika kombinationer — "heavysleetshowersandthunder" är fyra av dem i rad — och
/// en tabell över alla hade varit fyrtio rader att hålla i synk med någon annans lista. Ordningen
/// nedan är regeln: det som slår hårdast först, så att åska vinner över det den regnar med.
/// </para>
/// </remarks>
public static class WeatherWords
{
    /// <summary>Tecknet på hjälteraden.</summary>
    public static string Symbol(string code)
    {
        var (kind, isNight) = Read(code);

        return kind switch
        {
            Kind.Thunder => "⛈️",
            Kind.Snow => "❄️",
            Kind.Sleet => "🌨️",
            Kind.Rain => "🌧️",
            Kind.Fog => "🌫️",
            Kind.PartlyCloudy => "⛅",
            Kind.Cloudy => "☁️",
            Kind.Fair => isNight ? "🌙" : "🌤️",
            Kind.Clear => isNight ? "🌙" : "☀️",
            _ => "🌡️",
        };
    }

    /// <summary>Ordet skärmläsaren säger.</summary>
    public static string Spoken(string code) => Read(code).Kind switch
    {
        Kind.Thunder => "åska",
        Kind.Snow => "snö",
        Kind.Sleet => "snöblandat regn",
        Kind.Rain => "regn",
        Kind.Fog => "dimma",
        Kind.PartlyCloudy => "växlande molnighet",
        Kind.Cloudy => "mulet",
        Kind.Fair => "lätt molnighet",
        Kind.Clear => "klart",
        _ => string.Empty,
    };

    private enum Kind { Unknown, Clear, Fair, PartlyCloudy, Cloudy, Fog, Rain, Sleet, Snow, Thunder }

    private static (Kind Kind, bool IsNight) Read(string? code)
    {
        var text = code?.ToLowerInvariant() ?? string.Empty;
        var night = text.EndsWith("_night", StringComparison.Ordinal);

        var kind =
            text.Contains("thunder", StringComparison.Ordinal) ? Kind.Thunder :
            text.Contains("snow", StringComparison.Ordinal) ? Kind.Snow :
            text.Contains("sleet", StringComparison.Ordinal) ? Kind.Sleet :
            text.Contains("rain", StringComparison.Ordinal) ? Kind.Rain :
            text.Contains("fog", StringComparison.Ordinal) ? Kind.Fog :
            // Före "cloudy", som den innehåller.
            text.Contains("partlycloudy", StringComparison.Ordinal) ? Kind.PartlyCloudy :
            text.Contains("cloudy", StringComparison.Ordinal) ? Kind.Cloudy :
            text.Contains("fair", StringComparison.Ordinal) ? Kind.Fair :
            text.Contains("clearsky", StringComparison.Ordinal) ? Kind.Clear :
            Kind.Unknown;

        return (kind, night);
    }
}
