using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Backend.Eventor;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Backend.LiveResults;

/// <summary>
/// Live, as the app asks for it: by Eventor competition. Finding the LiveResults competition
/// behind that id is this class' actual work (SP-04); fetching the rows is the easy part.
/// </summary>
public sealed class LiveSource(
    LiveResultsClient _client,
    EventorSource _eventor,
    ResponseCache _cache,
    IOptions<LiveResultsOptions> _options,
    ILogger<LiveSource> _logger)
{
    private static readonly TimeSpan CalendarLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MatchLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ClassLifetime = TimeSpan.FromMinutes(5);

    private readonly LiveResultsOptions _settings = _options.Value;
    private readonly LiveResultsNormalizer _normalizer = LiveResultsNormalizer.ForZone(_options.Value.TimeZone);

    /// <summary>The upstream cache is 15 seconds, so a shorter one here would only cost data.</summary>
    private TimeSpan ResultLifetime => TimeSpan.FromSeconds(_settings.CacheSeconds);

    /// <summary>Today's competitions that actually have a live list behind them.</summary>
    public async Task<IReadOnlyList<Competition>> GetLiveCompetitionsAsync(
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var competitions = await _eventor.GetCompetitionsAsync(today, today, cancellationToken);
        var candidates = await CalendarAsync(cancellationToken);
        var onToday = candidates.Where(c => c.Date == today).ToList();

        var live = new List<Competition>();

        foreach (var competition in competitions)
        {
            if (CompetitionMatcher.Match(competition, onToday) is not null)
                live.Add(competition);
        }

        return live;
    }

    /// <summary>
    /// One class when the app asks for one, every class when it needs to find people across
    /// them — Min grupp runs in more than one class, and LiveResults is only searchable by
    /// class.
    /// </summary>
    public async Task<LiveSnapshot> GetSnapshotAsync(
        CompetitionId competition,
        string? className = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;

        if (await MatchAsync(competition, cancellationToken) is not { } match)
        {
            return new LiveSnapshot { Competition = competition, GeneratedAt = now, Entries = [] };
        }

        var classes = className is not null
            ? (IReadOnlyList<string>)[className]
            : await ClassesAsync(match.Competition.Id, cancellationToken);

        if (className is null && classes.Count > 8)
            _logger.LogInformation("Live: hämtar {Count} klasser för tävling {Id}.", classes.Count, match.Competition.Id);

        var organisations = await _eventor.DirectoryAsync(cancellationToken);

        var perClass = await Task.WhenAll(classes.Select(name =>
            EntriesAsync(match.Competition, name, organisations, cancellationToken)));

        return new LiveSnapshot
        {
            Competition = competition,
            GeneratedAt = now,
            Entries = [.. perClass.SelectMany(entries => entries)],
        };
    }

    /// <summary>
    /// Which LiveResults competition this Eventor event is. Cached for half an hour: the
    /// answer does not change during a race, and re-deriving it would mean pulling the whole
    /// national live calendar on every poll.
    /// </summary>
    public async Task<CompetitionMatch?> MatchAsync(CompetitionId competition, CancellationToken cancellationToken)
    {
        if (await _eventor.GetCompetitionAsync(competition, cancellationToken) is not { } found)
            return null;

        var candidates = await CalendarAsync(cancellationToken);

        return await _cache.GetOrAddAsync(
            $"live-match:{competition.Value}",
            MatchLifetime,
            _ => Task.FromResult(CompetitionMatcher.Match(found, candidates)),
            cancellationToken);
    }

    private Task<IReadOnlyList<LiveCompetition>> CalendarAsync(CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            "live-calendar",
            CalendarLifetime,
            async token => _normalizer.Competitions(await _client.GetAsync("getcompetitions", cancellationToken: token)),
            cancellationToken);

    private Task<IReadOnlyList<string>> ClassesAsync(int competition, CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"live-classes:{competition}",
            ClassLifetime,
            async token => _normalizer.Classes(await _client.GetAsync("getclasses", new Dictionary<string, string?>
            {
                ["comp"] = competition.ToString(CultureInfo.InvariantCulture),
            }, token)),
            cancellationToken);

    private Task<IReadOnlyList<LiveEntry>> EntriesAsync(
        LiveCompetition competition,
        string className,
        Eventor.OrganisationDirectory organisations,
        CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"live-results:{competition.Id}:{className}",
            ResultLifetime,
            async token =>
            {
                var payload = await _client.GetAsync("getclassresults", new Dictionary<string, string?>
                {
                    ["comp"] = competition.Id.ToString(CultureInfo.InvariantCulture),
                    ["class"] = className,
                    ["unformattedTimes"] = "true",
                }, token);

                return _normalizer.Entries(payload, className, competition.Date, organisations);
            },
            cancellationToken);
}
