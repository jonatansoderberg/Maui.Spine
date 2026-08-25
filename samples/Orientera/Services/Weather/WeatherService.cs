using System.Globalization;
using Orientera.Domain;

namespace Orientera.Services.Weather;

/// <summary>
/// Vädret på hälsningsraden: var läsaren är, vad det är för grader där, och vad det heter.
/// </summary>
/// <remarks>
/// Positionen tas i fallande ordning — senast kända läge, sedan en grov fix, sedan hemorten ur
/// <c>LocalIdentityStore</c>. Sista steget är alltid tillgängligt och alltid gott nog för en
/// temperatur, vilket är varför appen kan visa raden även för någon som aldrig ger den sin plats.
/// <para>
/// Riktig tid och inte <c>IClock</c>. Tidsmaskinen under Jag flyttar appens dygn så att det
/// seedade kalenderåret alltid är nu; vädret är ett påstående om den verkliga världen, och en
/// prognos hämtad mot ett påhittat klockslag vore ingen prognos.
/// </para>
/// </remarks>
public sealed class WeatherService(HttpClient _http, WeatherStore _store)
{
    /// <summary>
    /// MET Norways punktprognos. Fyra decimaler är deras egen gräns — fler avvisas, och mer
    /// precision än så säger ingenting om vädret ändå.
    /// </summary>
    private const string Endpoint = "weatherapi/locationforecast/2.0/compact?lat={0}&lon={1}";

    public async Task<CurrentWeather?> LoadAsync(Person me, bool mayAskForLocation)
    {
        var now = DateTimeOffset.Now;
        var cached = _store.Load();

        if (cached is not null && WeatherStore.IsFresh(cached, now))
            return cached;

        var (point, place) = await WhereAsync(me, mayAskForLocation);

        if (!point.IsKnown)
            return Fallback(cached, now);

        try
        {
            var url = string.Format(
                CultureInfo.InvariantCulture,
                Endpoint,
                Math.Round(point.Latitude, 4),
                Math.Round(point.Longitude, 4));

            var json = await _http.GetStringAsync(url);

            if (MetForecast.Parse(json, now, place) is not { } weather)
                return Fallback(cached, now);

            _store.Save(weather);
            return weather;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Utan nät svarar MET inte alls, och ett strypt anrop svarar 429. Båda är samma sak
            // för hälsningen: det som finns sparat, eller ingen rad.
            return Fallback(cached, now);
        }
    }

    private static CurrentWeather? Fallback(CurrentWeather? cached, DateTimeOffset now) =>
        cached is not null && WeatherStore.IsUsable(cached, now) ? cached : null;

    /// <summary>
    /// Var läsaren är, och vad platsen heter. Hemorten är sista steget och inte ett felfall:
    /// någon som nekat appen sin position ska få samma rad, inte en tom.
    /// </summary>
    private async Task<(GeoPoint Point, string Place)> WhereAsync(Person me, bool mayAsk)
    {
        var home = (me.Home, me.District);

        if (!await HasLocationPermissionAsync(mayAsk))
            return home;

        try
        {
            var location = await Geolocation.GetLastKnownLocationAsync()
                ?? await Geolocation.GetLocationAsync(new GeolocationRequest(
                    GeolocationAccuracy.Low, TimeSpan.FromSeconds(8)));

            if (location is null)
                return home;

            var point = new GeoPoint(location.Latitude, location.Longitude);

            return (point, await PlaceNameAsync(location) ?? me.District);
        }
        catch (Exception e) when (e is FeatureNotSupportedException
                                    or FeatureNotEnabledException
                                    or PermissionException)
        {
            return home;
        }
    }

    /// <summary>
    /// Frågan ställs aldrig vid första start. Den körningen har redan välkomstarket och sportvalet
    /// i kö, och en positionsdialog som tredje ruta i följd är hur man lär någon att trycka
    /// "Neka". Nekas den ställs den heller aldrig igen — appen faller tillbaka på hemorten och
    /// raden ser likadan ut.
    /// </summary>
    private static async Task<bool> HasLocationPermissionAsync(bool mayAsk)
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (status is PermissionStatus.Granted)
                return true;

            if (!mayAsk || status is PermissionStatus.Denied)
                return false;

            return await Permissions.RequestAsync<Permissions.LocationWhenInUse>()
                is PermissionStatus.Granted;
        }
        catch (Exception e) when (e is FeatureNotSupportedException or PermissionException)
        {
            return false;
        }
    }

    private static async Task<string?> PlaceNameAsync(Location location)
    {
        try
        {
            var placemarks = await Geocoding.GetPlacemarksAsync(location);

            return placemarks?.FirstOrDefault() is { } placemark
                ? placemark.Locality ?? placemark.SubAdminArea ?? placemark.AdminArea
                : null;
        }
        catch (Exception)
        {
            // Brett, och avsiktligt. Plattformens geokodare är en tredjepartsslagning som
            // misslyckas på fler sätt än den dokumenterar — CLGeocoder svarar med sitt eget
            // NSError, Androids med en IOException — och namnet är en bekvämlighet. Distriktet
            // står redan redo som svar, och en hälsning ska inte kunna fälla sidan.
            return null;
        }
    }
}
