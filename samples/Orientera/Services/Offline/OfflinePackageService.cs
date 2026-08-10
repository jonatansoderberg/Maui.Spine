using Orientera.Domain;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Services.Offline;

/// <summary>Where a piece of competition data came from. Always shown, never implied.</summary>
public enum DataOrigin
{
    Live,
    Cache,
    Unavailable,
}

/// <summary>
/// One competition as the UI needs it, together with where it came from and how old it is.
/// </summary>
public sealed record CompetitionSnapshot
{
    public required DataOrigin Origin { get; init; }
    public Competition? Competition { get; init; }
    public Start? MyStart { get; init; }
    public DateTimeOffset? MyEntryRegisteredAt { get; init; }
    public DateTimeOffset? GroupEntryRegisteredAt { get; init; }
    public IReadOnlyList<Start> GroupStarts { get; init; } = [];
    public IReadOnlyList<CompetitionResult> Results { get; init; } = [];
    public Prediction? Prediction { get; init; }

    /// <summary>Set when <see cref="Origin"/> is <see cref="DataOrigin.Cache"/>.</summary>
    public DateTimeOffset? CachedAt { get; init; }

    public static CompetitionSnapshot Unavailable() => new() { Origin = DataOrigin.Unavailable };
}

/// <summary>
/// Builds and serves the offline competition package.
/// </summary>
/// <remarks>
/// The rule from the product principles: bad coverage must not take out critical race
/// information. So a competition the user is entered in, follows, or has starred is assembled
/// into a package while there is a connection, and served from that package when there is not.
/// </remarks>
public sealed class OfflinePackageService(
    IClock _clock,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    IOfflineStore _store)
{
    /// <summary>
    /// Reads a competition live, falling back to the stored package. Returns
    /// <see cref="DataOrigin.Unavailable"/> only when the source is down *and* nothing was cached.
    /// </summary>
    public async Task<CompetitionSnapshot> GetAsync(
        CompetitionId competition,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var live = await BuildAsync(competition, cancellationToken);

            if (live is null)
                return CompetitionSnapshot.Unavailable();

            // Reading it is also the cheapest moment to refresh what is stored.
            await _store.SaveAsync(live, cancellationToken);

            return new CompetitionSnapshot
            {
                Origin = DataOrigin.Live,
                Competition = live.Competition,
                MyStart = live.MyStart,
                MyEntryRegisteredAt = live.MyEntryRegisteredAt,
                GroupEntryRegisteredAt = live.GroupEntryRegisteredAt,
                GroupStarts = live.GroupStarts,
                Results = live.Results,
                Prediction = live.Prediction,
            };
        }
        catch (SourceUnavailableException)
        {
            return await FromCacheAsync(competition, cancellationToken);
        }
    }

    private async Task<CompetitionSnapshot> FromCacheAsync(
        CompetitionId competition,
        CancellationToken cancellationToken)
    {
        var cached = await _store.GetAsync(competition, cancellationToken);

        if (cached is null)
            return CompetitionSnapshot.Unavailable();

        return new CompetitionSnapshot
        {
            Origin = DataOrigin.Cache,
            Competition = cached.Competition,
            MyStart = cached.MyStart,
            MyEntryRegisteredAt = cached.MyEntryRegisteredAt,
            GroupEntryRegisteredAt = cached.GroupEntryRegisteredAt,
            GroupStarts = cached.GroupStarts,
            Results = cached.Results,
            Prediction = cached.Prediction,
            CachedAt = cached.CachedAt,
        };
    }

    /// <summary>
    /// Refreshes the packages worth keeping: what I am entered in, what someone in Min grupp is
    /// entered in, and what I have starred. Called opportunistically while there is coverage.
    /// </summary>
    public async Task<int> RefreshRelevantAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var me = await _people.GetMeAsync(cancellationToken);
            var group = (await _people.GetMyGroupAsync(cancellationToken)).Select(f => f.Person.Id).ToHashSet();
            var entries = await _participation.GetEntriesAsync(cancellationToken);
            var favourites = await _events.GetFavouritesAsync(cancellationToken);

            var relevant = entries
                .Where(e => e.Person == me.Id || group.Contains(e.Person))
                .Select(e => e.Competition)
                .Concat(favourites)
                .Distinct()
                .ToList();

            var saved = 0;

            foreach (var id in relevant)
            {
                if (await BuildAsync(id, cancellationToken) is { } package)
                {
                    await _store.SaveAsync(package, cancellationToken);
                    saved++;
                }
            }

            return saved;
        }
        catch (SourceUnavailableException)
        {
            // Nothing to refresh from; the packages already stored stay valid.
            return 0;
        }
    }

    private async Task<CompetitionPackage?> BuildAsync(CompetitionId id, CancellationToken cancellationToken)
    {
        var competition = await _events.GetCompetitionAsync(id, cancellationToken);

        if (competition is null)
            return null;

        var me = await _people.GetMeAsync(cancellationToken);
        var group = (await _people.GetMyGroupAsync(cancellationToken)).Select(f => f.Person.Id).ToHashSet();
        var starts = await _participation.GetStartsAsync(id, cancellationToken);
        var entries = await _participation.GetEntriesAsync(cancellationToken);

        return new CompetitionPackage
        {
            Competition = competition,
            CachedAt = _clock.Now,
            MyStart = starts.FirstOrDefault(s => s.Person == me.Id),
            MyEntryRegisteredAt = entries
                .FirstOrDefault(e => e.Competition == id && e.Person == me.Id)?.RegisteredAt,
            GroupEntryRegisteredAt = entries
                .Where(e => e.Competition == id && group.Contains(e.Person))
                .OrderBy(e => e.RegisteredAt)
                .FirstOrDefault()?.RegisteredAt,
            GroupStarts = starts.Where(s => group.Contains(s.Person)).ToList(),
            Results = await _participation.GetResultsAsync(id, cancellationToken),
            Prediction = await _participation.GetPredictionAsync(id, me.Id, cancellationToken),
        };
    }
}
