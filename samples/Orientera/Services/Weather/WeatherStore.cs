using System.Text.Json;

namespace Orientera.Services.Weather;

/// <summary>
/// Det senast hämtade vädret, på telefonen.
/// </summary>
/// <remarks>
/// Två åldrar och inte en. Under <see cref="Fresh"/> duger det sparade svaret och nätet får vara
/// i fred — hälsningen ska inte kosta ett anrop varje gång man öppnar appen. Mellan <c>Fresh</c>
/// och <see cref="Stale"/> visas det bara när hämtningen misslyckades, vilket är vad "offline"
/// betyder här. Äldre än så är det inte väder längre, och då står raden hellre tom: gårdagens
/// tolv grader är ett påstående om i dag som ingen bett om.
/// </remarks>
public sealed class WeatherStore(string _path)
{
    /// <summary>Så länge det sparade svaret används utan att fråga nätet.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(30);

    /// <summary>Så länge det duger som sista utväg när hämtningen inte gick.</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromHours(12);

    private CurrentWeather? _cached;
    private bool _read;

    public CurrentWeather? Load()
    {
        if (_read)
            return _cached;

        _read = true;

        try
        {
            if (File.Exists(_path))
                _cached = JsonSerializer.Deserialize<CurrentWeather>(File.ReadAllText(_path));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            _cached = null;
        }

        return _cached;
    }

    public void Save(CurrentWeather weather)
    {
        _cached = weather;
        _read = true;

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(weather));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Ett väder som inte gick att spara är fortfarande ett väder att visa.
        }
    }

    /// <summary>
    /// Åldern mäts som avstånd och inte som differens. Tidsmaskinen under Jag flyttar appens
    /// dygn, och ett sparat väder stämplat med riktig tid ligger då i framtiden — en rå
    /// subtraktion hade gjort det evigt färskt.
    /// </summary>
    public static bool IsFresh(CurrentWeather weather, DateTimeOffset now) =>
        (now - weather.ReadAt).Duration() < Fresh;

    public static bool IsUsable(CurrentWeather weather, DateTimeOffset now) =>
        (now - weather.ReadAt).Duration() < Stale;
}
