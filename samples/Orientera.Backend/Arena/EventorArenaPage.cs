using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Orientera.Backend.Arena;

/// <summary>Det tävlingssidan vet om platsen: arenan, och arrangörens eget tävlingsområde om det finns.</summary>
public sealed record ArenaPageFacts(
    (double Latitude, double Longitude)? Arena,
    IReadOnlyList<(double Latitude, double Longitude)>? Area);

/// <summary>
/// Läser arenaposition och tävlingsområde ur Eventors publika tävlingssida.
/// </summary>
/// <remarks>
/// API:et bär tävlingens namn och tider, men tävlingsområdets polygon ligger bara i sidan,
/// som "förbjudet område" — frivilligt för arrangören och frånvarande i knappt hälften av
/// tävlingarna. Anropare måste klara <c>Area = null</c>.
/// </remarks>
public sealed partial class EventorArenaPage(HttpClient _http, ILogger<EventorArenaPage> _logger)
{
    public async Task<ArenaPageFacts> FetchAsync(string eventId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://eventor.orientering.se/Events/Show/{Uri.EscapeDataString(eventId)}");
        request.Headers.UserAgent.ParseAdd("orientera-backend/1.0");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var facts = Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        _logger.LogInformation("Eventorsidan för {EventId}: arena {Arena}, område {Area}.",
            eventId, facts.Arena is null ? "saknas" : "finns",
            facts.Area is null ? "saknas" : $"{facts.Area.Count} hörn");
        return facts;
    }

    /// <summary>
    /// Arenan är sidans kartcentrum. Citattecknen runt värdet skiljer den från polygonens
    /// hörn, som ligger som rena tal.
    /// </summary>
    public static ArenaPageFacts Parse(string html)
    {
        var latitude = CenterLatitude().Match(html);
        var longitude = CenterLongitude().Match(html);
        (double, double)? arena = latitude.Success && longitude.Success
            ? (Number(latitude.Groups[1].Value), Number(longitude.Groups[1].Value))
            : null;

        var corners = AreaCorner().Matches(html)
            .Select(m => (Number(m.Groups[2].Value), Number(m.Groups[1].Value)))
            .ToList();
        // Sluten ring: sista hörnet upprepar det första.
        if (corners.Count > 2 && corners[0] == corners[^1])
            corners.RemoveAt(corners.Count - 1);

        return new ArenaPageFacts(arena, corners.Count >= 3 ? corners : null);
    }

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"centerLatitude&quot;:&quot;([-\d.]+)")]
    private static partial Regex CenterLatitude();

    [GeneratedRegex(@"centerLongitude&quot;:&quot;([-\d.]+)")]
    private static partial Regex CenterLongitude();

    [GeneratedRegex(@"Longitude&quot;:([-\d.]+),&quot;Latitude&quot;:([-\d.]+)")]
    private static partial Regex AreaCorner();
}
