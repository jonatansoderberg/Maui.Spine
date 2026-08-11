using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Services.Offline;

/// <summary>
/// Sits where the network is. Every source call passes through here, and fails when
/// <see cref="ConnectivitySwitch.IsOffline"/> is set — the dev switch that makes the offline
/// and error paths demonstrable without unplugging anything. A real failure below it arrives
/// as the same <see cref="SourceUnavailableException"/> and takes the same path.
/// </summary>
public sealed class UnreliableSource(IOrienteraSource _inner, ConnectivitySwitch _connectivity) : IOrienteraSource
{
    private void Guard()
    {
        if (_connectivity.IsOffline)
            throw new SourceUnavailableException("Ingen anslutning till Orienteras datakällor.");
    }

    private Task<T> Through<T>(Func<Task<T>> call)
    {
        Guard();
        return call();
    }

    // ---------------------------------------------------------------- IEventSource

    public Task<IReadOnlyList<Competition>> GetCompetitionsAsync(CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetCompetitionsAsync(cancellationToken));

    public Task<Competition?> GetCompetitionAsync(CompetitionId id, CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetCompetitionAsync(id, cancellationToken));

    public Task<Course?> GetCourseAsync(CompetitionId id, string className, CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetCourseAsync(id, className, cancellationToken));

    public Task<Series?> GetSeriesAsync(SeriesId id, CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetSeriesAsync(id, cancellationToken));

    // Favourites are local, so they keep working without a connection.
    public Task<IReadOnlySet<CompetitionId>> GetFavouritesAsync(CancellationToken cancellationToken = default) =>
        _inner.GetFavouritesAsync(cancellationToken);

    public Task<bool> ToggleFavouriteAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        _inner.ToggleFavouriteAsync(competition, cancellationToken);

    // ---------------------------------------------------------------- IPeopleSource

    // Who I am and who I follow is local data too — the app works without an account.
    public Task<Person> GetMeAsync(CancellationToken cancellationToken = default) =>
        _inner.GetMeAsync(cancellationToken);

    public Task<IReadOnlyList<FollowedPerson>> GetMyGroupAsync(CancellationToken cancellationToken = default) =>
        _inner.GetMyGroupAsync(cancellationToken);

    public Task<IReadOnlyList<Person>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        Through(() => _inner.SearchAsync(query, cancellationToken));

    public Task FollowAsync(Person person, FollowReason reason, CancellationToken cancellationToken = default) =>
        _inner.FollowAsync(person, reason, cancellationToken);

    public Task UnfollowAsync(PersonId person, CancellationToken cancellationToken = default) =>
        _inner.UnfollowAsync(person, cancellationToken);

    // ---------------------------------------------------------------- IParticipationSource

    public Task<IReadOnlyList<CompetitionEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetEntriesAsync(cancellationToken));

    public Task<IReadOnlyList<Start>> GetStartsAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetStartsAsync(competition, cancellationToken));

    public Task<IReadOnlyList<CompetitionResult>> GetResultsAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetResultsAsync(competition, cancellationToken));

    public Task<IReadOnlyList<CompetitionResult>> GetResultsForPersonAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetResultsForPersonAsync(person, cancellationToken));

    public Task<Prediction?> GetPredictionAsync(CompetitionId competition, PersonId person, CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetPredictionAsync(competition, person, cancellationToken));

    // ---------------------------------------------------------------- ILiveSource

    public Task<IReadOnlyList<Competition>> GetLiveCompetitionsAsync(CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetLiveCompetitionsAsync(cancellationToken));

    public Task<LiveSnapshot> GetSnapshotAsync(
        CompetitionId competition,
        string? className = null,
        CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetSnapshotAsync(competition, className, cancellationToken));

    // ---------------------------------------------------------------- IProgressSource

    public Task<RankingSnapshot?> GetRankingAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetRankingAsync(person, cancellationToken));

    public Task<IReadOnlyList<SeriesStanding>> GetSeriesStandingsAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Through(() => _inner.GetSeriesStandingsAsync(person, cancellationToken));
}
