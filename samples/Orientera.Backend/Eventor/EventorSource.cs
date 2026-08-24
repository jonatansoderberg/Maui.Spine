using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Domain;

namespace Orientera.Backend.Eventor;

/// <summary>
/// What the BFF serves: normalised competitions, starts and results, each behind a cache
/// lifetime chosen for how fast the underlying thing actually changes.
/// </summary>
/// <remarks>
/// This is deliberately not an <c>IEventSource</c>. The app's source interfaces also carry
/// local concerns — interests, who I am — that a backend has no business answering in M1.
/// </remarks>
public sealed class EventorSource(EventorClient _client, ResponseCache _cache, IOptions<EventorOptions> _options)
{
    private static readonly TimeSpan CalendarLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EventLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OrganisationLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartListLifetime = TimeSpan.FromMinutes(5);

    // Results move while the competition is running, and a runner refreshing at the arena is
    // exactly the case the cache must not make stale.
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromMinutes(1);

    /// <summary>Once the arena has closed the list has said what it has to say.</summary>
    private static readonly TimeSpan FinishedResultLifetime = TimeSpan.FromHours(6);

    private readonly EventorOptions _settings = _options.Value;
    private readonly EventorNormalizer _normalizer = EventorNormalizer.ForZone(_options.Value.TimeZone);
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(_options.Value.TimeZone);

    public async Task<IReadOnlyList<Competition>> GetCompetitionsAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? today.AddDays(-_settings.CalendarDaysBack);
        var end = to ?? today.AddDays(_settings.CalendarDaysAhead);

        var directory = await DirectoryAsync(cancellationToken);

        var events = await _cache.GetOrAddAsync(
            $"events:{start:O}:{end:O}:{_settings.OrganisationIds}",
            CalendarLifetime,
            token => _client.GetAsync("events", new Dictionary<string, string?>
            {
                ["fromDate"] = Moment(start, TimeOnly.MinValue),
                ["toDate"] = Moment(end, TimeOnly.MaxValue),
                ["organisationIds"] = _settings.OrganisationIds,
                ["includeEntryBreaks"] = "true",
            }, token),
            cancellationToken);

        return _normalizer.Competitions(events, directory);
    }

    /// <summary>
    /// The detail view is a competition plus everything a runner opens it for: the documents,
    /// the classes, and whether there are split times to analyse.
    /// </summary>
    public async Task<Competition?> GetCompetitionAsync(CompetitionId id, CancellationToken cancellationToken = default)
    {
        var directory = await DirectoryAsync(cancellationToken);

        var element = await _cache.GetOrAddAsync(
            $"event:{id.Value}",
            EventLifetime,
            token => _client.GetAsync($"event/{id.Value}", cancellationToken: token),
            cancellationToken);

        var root = element.Name.LocalName == "Event" ? element : element.Children("Event").FirstOrDefault();

        if (root is null || _normalizer.Competition(root, directory) is not { } competition)
            return null;

        var documents = await _cache.GetOrAddAsync(
            $"documents:{id.Value}",
            EventLifetime,
            token => _client.GetAsync("events/documents", new Dictionary<string, string?>
            {
                ["eventIds"] = id.Value,
            }, token),
            cancellationToken);

        var classes = await _cache.GetOrAddAsync(
            $"classes:{id.Value}",
            EventLifetime,
            token => _client.GetAsync("eventclasses", new Dictionary<string, string?>
            {
                ["eventId"] = id.Value,
            }, token),
            cancellationToken);

        var detail = competition with
        {
            Documents = _normalizer.Documents(documents, id),
            Classes = _normalizer.Classes(classes),
            Schedule = await WithSplitsAsync(competition, id, cancellationToken),
        };

        return WithFirstStart(detail, await FirstStartAsync(detail, id, cancellationToken));
    }

    public async Task<IReadOnlyList<Start>> GetStartsAsync(CompetitionId id, CancellationToken cancellationToken = default)
    {
        var starts = await _cache.GetOrAddAsync(
            $"starts:{id.Value}",
            StartListLifetime,
            token => _client.GetAsync("starts/event", new Dictionary<string, string?>
            {
                ["eventId"] = id.Value,
            }, token),
            cancellationToken);

        return _normalizer.Starts(starts, id);
    }

    public async Task<IReadOnlyList<CompetitionResult>> GetResultsAsync(CompetitionId id, CancellationToken cancellationToken = default)
    {
        var results = await ResultsAsync(id, top: null, cancellationToken);
        return _normalizer.Results(results, id, await DirectoryAsync(cancellationToken));
    }

    /// <summary>
    /// One class' rows out of a competition's result list.
    /// </summary>
    /// <remarks>
    /// Eventor offers nothing narrower: <c>results/event</c> takes no class, and
    /// <c>wrsresults/event</c> — which does — answers 404 for anything outside the world ranking.
    /// So the whole list is fetched once, normalised once, and every class is served out of that
    /// one copy.
    /// <para>
    /// Two things make that affordable. Split times are left out, which is where the weight sits:
    /// O-Ringen's list is 86 MB and 97 seconds with them, and a participant list wants a placing
    /// and a time, never a leg — the runner who opens their own analysis asks
    /// <c>results/person</c>, which carries them for one person. And a published list stops
    /// changing, so once the arena has closed the copy is kept for hours instead of the minute a
    /// running race needs.
    /// </para>
    /// Only the normalised list is kept, not the document it came from. Caching both would hold
    /// the same competition in memory twice, and the XML is by far the larger half.
    /// </remarks>
    public async Task<IReadOnlyList<CompetitionResult>> GetClassResultsAsync(
        CompetitionId id, string className, bool splits = false, CancellationToken cancellationToken = default)
    {
        if (className.Length == 0)
            return [];

        var directory = await DirectoryAsync(cancellationToken);

        var all = await _cache.GetOrAddAsync(
            $"class-results:{id.Value}:{splits}",
            await ResultLifetimeFor(id, cancellationToken),
            async token => _normalizer.Results(
                await _client.GetAsync("results/event", new Dictionary<string, string?>
                {
                    ["eventId"] = id.Value,
                    ["includeSplitTimes"] = splits ? "true" : "false",
                }, token),
                id,
                directory),
            cancellationToken);

        return [.. all.Where(result => InClass(result, className))];
    }

    /// <summary>
    /// Whether a row belongs to the class that was asked for.
    /// </summary>
    /// <remarks>
    /// A multi-race event names each race's rows after the race — Karlstad Indoor's one class
    /// becomes "Herrar, Etapp 1" and "Herrar, Etapp 2" — so the class asked for is a prefix of
    /// what the row carries rather than the whole of it. Both stages are the same class and both
    /// belong in the list.
    /// </remarks>
    private static bool InClass(CompetitionResult result, string className) =>
        result.Class.Equals(className, StringComparison.OrdinalIgnoreCase)
        || result.Class.StartsWith($"{className}, ", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How long a competition's result list may be kept. A race that is over has stopped moving;
    /// one that is still running has not, and the runner refreshing at the arena is the reader
    /// the short lifetime exists for.
    /// </summary>
    private async Task<TimeSpan> ResultLifetimeFor(CompetitionId id, CancellationToken cancellationToken) =>
        await GetCompetitionAsync(id, cancellationToken) is { } competition
            && competition.LastFinish < DateTimeOffset.Now
                ? FinishedResultLifetime
                : ResultLifetime;

    /// <summary>
    /// A person's own results in the given events — their row, and the size of the class it
    /// stood in, for every event in one request.
    /// </summary>
    /// <remarks>
    /// The narrow question, and the one the results list actually asks: it wants "how large was
    /// the field" for a season of races, and was answering it by fetching each competition whole.
    /// Split times are left out; a placement and a field size need none of them.
    /// </remarks>
    public async Task<IReadOnlyList<CompetitionResult>> GetPersonResultsAsync(
        string personId,
        IReadOnlyList<CompetitionId> events,
        int? top = null,
        bool splits = false,
        CancellationToken cancellationToken = default)
    {
        if (personId.Length == 0)
            return [];

        var ids = string.Join(',', events.Select(e => e.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        var results = await _cache.GetOrAddAsync(
            $"person-results:{personId}:{ids}:{top}:{splits}",
            ResultLifetime,
            token => _client.GetAsync("results/person", new Dictionary<string, string?>
            {
                ["personId"] = personId,
                ["eventIds"] = ids.Length > 0 ? ids : null,
                ["includeSplitTimes"] = splits ? "true" : "false",
                ["top"] = top?.ToString(CultureInfo.InvariantCulture),
            }, token),
            cancellationToken);

        return _normalizer.PersonResults(results, await DirectoryAsync(cancellationToken));
    }

    /// <summary>
    /// Split times are published separately from results, and the calendar does not say when.
    /// One result is enough to find out, and it is the same cached document the analysis view
    /// will ask for.
    /// </summary>
    private async Task<CompetitionSchedule> WithSplitsAsync(
        Competition competition,
        CompetitionId id,
        CancellationToken cancellationToken)
    {
        if (competition.Schedule.ResultsPublishedAt is not { } publishedAt)
            return competition.Schedule;

        var probe = await ResultsAsync(id, top: 1, cancellationToken);

        bool hasSplits = _normalizer.Results(probe, id).Any(r => r.Splits.Count > 0);

        return competition.Schedule with { SplitsPublishedAt = hasSplits ? publishedAt : null };
    }

    /// <summary>
    /// Moving the first start has to move the arena's closing time with it. The calendar gives
    /// the date at midnight, so a competition whose start list says 18:30 would otherwise close
    /// at six in the morning — twelve hours before it began.
    /// </summary>
    private static Competition WithFirstStart(Competition competition, DateTimeOffset firstStart) =>
        competition with
        {
            FirstStart = firstStart,
            LastFinish = competition.LastFinish > firstStart ? competition.LastFinish : firstStart.AddHours(6),
        };

    /// <summary>
    /// The calendar carries the competition's date at midnight — the first start is not in it.
    /// Once the start list is out, the earliest start is the real answer.
    /// </summary>
    private async Task<DateTimeOffset> FirstStartAsync(
        Competition competition,
        CompetitionId id,
        CancellationToken cancellationToken)
    {
        if (competition.Schedule.StartListPublishedAt is null)
            return competition.FirstStart;

        var starts = await GetStartsAsync(id, cancellationToken);

        return starts.Count > 0 ? starts.Min(s => s.StartTime) : competition.FirstStart;
    }

    private Task<XElement> ResultsAsync(CompetitionId id, int? top, CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"results:{id.Value}:{top}",
            ResultLifetime,
            token => _client.GetAsync("results/event", new Dictionary<string, string?>
            {
                ["eventId"] = id.Value,
                ["includeSplitTimes"] = "true",
                ["top"] = top?.ToString(CultureInfo.InvariantCulture),
            }, token),
            cancellationToken);

    /// <summary>Clubs, districts and badges change a few times a year — once a day is enough.</summary>
    public Task<OrganisationDirectory> DirectoryAsync(CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            "organisations",
            OrganisationLifetime,
            async token => OrganisationDirectory.From(await _client.GetAsync("organisations", cancellationToken: token)),
            cancellationToken);

    /// <summary>
    /// Eventor's guide is explicit that input parameters are always UTC, while the answers come
    /// back in Swedish local time. A calendar window is a Swedish day, so it is converted here.
    /// </summary>
    private string Moment(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        var offset = new DateTimeOffset(local, _zone.GetUtcOffset(local));

        return offset.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
