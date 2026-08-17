using Microsoft.Extensions.Logging;
using Orientera.Backend.Caching;
using Orientera.Domain;
using Orientera.Domain.Eventor;

namespace Orientera.Backend.Eventor;

/// <summary>
/// Who has entered a competition, before anybody has been drawn a start time.
/// </summary>
/// <remarks>
/// Eventor's API answers 403 for entries with a club's key, so this reads the public web page the
/// federation publishes for the same thing. Public is the operative word: the page is identical
/// with no login, which is why it is fetched here once for everyone rather than on each phone with
/// the reader's own session. Personal reading stayed on the device in #123; this is not personal.
///
/// Fifteen minutes. Entries arrive steadily until the deadline and the count beside the class is
/// the thing people watch, but a national event's page is ninety kilobytes and nobody needs it to
/// the second.
/// </remarks>
public sealed class EntryListSource(
    HttpClient _http,
    ResponseCache _cache,
    ILogger<EntryListSource> _logger)
{
    public async Task<IReadOnlyList<StartFieldRunner>> ForClassAsync(
        string eventId, string? className, CancellationToken cancellationToken)
    {
        var entrants = await _cache.GetOrAddAsync(
            $"entries:{eventId}",
            TimeSpan.FromMinutes(15),
            token => FetchAsync(eventId, token),
            cancellationToken);

        return
        [
            .. entrants
                .Where(e => className is null || e.Class.Equals(className, StringComparison.OrdinalIgnoreCase))
                .Select(e => new StartFieldRunner
                {
                    // The page states no person id. An entry list row is a name and a club, and
                    // the app matches the reader by those rather than by an id nobody published.
                    Person = new PersonId($"anon:{e.Class}:{e.Name}"),
                    Name = e.Name,
                    Club = e.Club,
                }),
        ];
    }

    private async Task<IReadOnlyList<EventorEntrant>> FetchAsync(
        string eventId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(
                $"Events/Entries?eventId={Uri.EscapeDataString(eventId)}&groupBy=EventClass",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return [];

            return EntryListPageParser.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        // An entry list that will not load is a section the app leaves out, not a failed page.
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Anmälningslistan för {Event} kunde inte hämtas.", eventId);

            return [];
        }
    }
}
