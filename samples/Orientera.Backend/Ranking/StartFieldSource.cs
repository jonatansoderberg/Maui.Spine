using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Backend.Eventor;
using Orientera.Domain;

namespace Orientera.Backend.Ranking;

/// <summary>
/// A start field, sorted by Sverigelistan.
/// </summary>
/// <remarks>
/// The start list carries names, person ids and clubs; Sverigelistan's club pages carry points for
/// everyone in a club. One page per club in the field, not one per runner — a field of forty spans
/// a dozen clubs, and the pages are cached for half a day and shared between classes and users.
///
/// Read through <see cref="EventorSession"/>, because anonymously a club page lists almost nobody
/// (measured on Gävle OK: one runner against 188). That is the same prototype boundary as the rest
/// of the ranking, and it is governed by the same setting.
/// </remarks>
public sealed class StartFieldSource(
    EventorClient _eventor,
    EventorSession _sessions,
    ResponseCache _cache,
    IOptions<RankingOptions> _ranking,
    ILogger<StartFieldSource> _logger)
{
    public Task<IReadOnlyList<StartFieldRunner>> ForClassAsync(
        string eventId, string className, CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"field:{eventId}:{className}",
            TimeSpan.FromHours(1),
            token => FetchAsync(eventId, className, token),
            cancellationToken);

    private async Task<IReadOnlyList<StartFieldRunner>> FetchAsync(
        string eventId, string className, CancellationToken cancellationToken)
    {
        try
        {
            var starts = await _eventor.GetAsync(
                "starts/event",
                new Dictionary<string, string?> { ["eventId"] = eventId },
                cancellationToken);

            var field = Field(starts, className);

            if (field.Count == 0)
                return [];

            var ranking = await RankingByRunnerAsync(field, cancellationToken);

            return
            [
                .. field
                    .Select(entry => ranking.TryGetValue(entry.Runner.Person.Value, out var row)
                        ? entry.Runner with { Points = row.Points, NationalRank = row.NationalRank }
                        : entry.Runner)
                    // Lower points is a better runner. Whoever the list does not carry goes last,
                    // in start order, rather than being given a place they have not earned.
                    .OrderBy(r => r.Points ?? double.MaxValue)
                    .ThenBy(r => r.StartTime ?? DateTimeOffset.MaxValue),
            ];
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Startfältet kunde inte hämtas för {Event} {Class}.", eventId, className);

            return [];
        }
    }

    /// <summary>The runners in one class, as the start list states them, with their club's id.</summary>
    internal static List<(StartFieldRunner Runner, string? Club)> Field(XElement starts, string className)
    {
        var field = new List<(StartFieldRunner, string?)>();

        foreach (var classStart in starts.Deep("ClassStart"))
        {
            var name = classStart.Child("EventClass").Text("Name")
                ?? classStart.Child("EventClass").Text("ClassShortName");

            if (!string.Equals(name, className, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var personStart in classStart.Children("PersonStart"))
            {
                var person = personStart.Child("Person");

                if (person.Text("PersonId") is not { Length: > 0 } id)
                    continue;

                var given = string.Join(' ', person.Child("PersonName").Children("Given").Select(g => g.Value.Trim()));
                var family = person.Child("PersonName").Text("Family") ?? string.Empty;

                field.Add((
                    new StartFieldRunner
                    {
                        Person = new PersonId(id),
                        Name = string.Join(' ', new[] { given, family }.Where(p => p.Length > 0)),
                        Club = personStart.Child("Organisation").Text("Name") ?? string.Empty,
                        StartTime = personStart.Child("Start").Child("StartTime").Moment(TimeZoneInfo.Local),
                    },
                    personStart.Child("Organisation").Text("OrganisationId")));
            }
        }

        return field;
    }

    /// <summary>
    /// Every club in the field, looked up once. Runners whose club page does not list them keep
    /// no ranking at all, which is the honest answer for someone the list does not rank.
    /// </summary>
    private async Task<Dictionary<string, RankingRow>> RankingByRunnerAsync(
        IReadOnlyList<(StartFieldRunner Runner, string? Club)> field, CancellationToken cancellationToken)
    {
        var rows = new Dictionary<string, RankingRow>(StringComparer.Ordinal);

        if (_ranking.Value.DemoSessionPersonId is not { Length: > 0 } person)
            return rows;

        // The start list states the club's id outright, so no directory lookup is needed.
        var ids = field.Select(e => e.Club).OfType<string>().Distinct().ToList();

        if (ids.Count == 0)
            return rows;

        using var session = await _sessions.OpenAsync(person, cancellationToken);

        if (session is null)
            return rows;

        // A field of forty spans a couple of dozen clubs, and one page took a second each when
        // fetched in turn — close enough to the app's timeout to lose the whole section. Four at
        // a time is fast enough and still a polite number of requests to send Eventor at once.
        var pages = new List<IReadOnlyList<RankingRow>>();

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (club, token) =>
            {
                var page = await ClubAsync(session, club, token);

                lock (pages)
                    pages.Add(page);
            });

        foreach (var row in pages.SelectMany(p => p))
            rows.TryAdd(row.RunnerId, row);

        return rows;
    }

    private Task<IReadOnlyList<RankingRow>> ClubAsync(
        HttpClient session, string clubId, CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"ranking:club:session:{clubId}",
            TimeSpan.FromHours(_ranking.Value.CacheHours),
            async token =>
            {
                using var page = await session.GetAsync(
                    new Uri(new Uri(_ranking.Value.BaseAddress), $"Ranking/ol/Club/Index/{clubId}"), token);

                return page.IsSuccessStatusCode
                    ? RankingPageParser.Parse(clubId, await page.Content.ReadAsStringAsync(token))
                    : [];
            },
            cancellationToken);
}
