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
/// local concerns — favourites, who I am — that a backend has no business answering in M1.
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

    private readonly EventorOptions _settings = _options.Value;
    private readonly EventorNormalizer _normalizer = EventorNormalizer.ForZone(_options.Value.TimeZone);

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

        return competition with
        {
            Documents = _normalizer.Documents(documents, id),
            Classes = _normalizer.Classes(classes),
            Schedule = await WithSplitsAsync(competition, id, cancellationToken),
        };
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
        return _normalizer.Results(results, id);
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

    /// <summary>Clubs and districts change a few times a year, so once a day is often enough.</summary>
    private Task<OrganisationDirectory> DirectoryAsync(CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            "organisations",
            OrganisationLifetime,
            async token => OrganisationDirectory.From(await _client.GetAsync("organisations", cancellationToken: token)),
            cancellationToken);

    private static string Moment(DateOnly date, TimeOnly time) =>
        date.ToDateTime(time).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
