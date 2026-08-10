using System.Net;
using System.Net.Http.Json;
using Orientera.Domain;
using Orientera.Services.Offline;

namespace Orientera.Services.Sources;

/// <summary>
/// The app against <c>Orientera.Backend</c>. Competitions, starts and results come from
/// Eventor through the BFF; the rest is answered without inventing anything.
/// </summary>
/// <remarks>
/// Three kinds of data meet here. What M1 integrates comes over HTTP. What is local by
/// principle — who I am, who I follow, what I have starred — stays local and keeps working
/// without a connection or an account. What M2 and M3 will integrate — my entries, live,
/// prediction, Sverigelistan — is empty rather than borrowed from the fake dataset: a real
/// calendar next to a fabricated entry would be worse than an honest empty state.
/// </remarks>
public sealed class BackendSource(HttpClient _http, IOrienteraSource _local) : IOrienteraSource
{
    // ---------------------------------------------------------------- IEventSource

    public Task<IReadOnlyList<Competition>> GetCompetitionsAsync(CancellationToken cancellationToken = default) =>
        ListAsync<Competition>("competitions", cancellationToken);

    public Task<Competition?> GetCompetitionAsync(CompetitionId id, CancellationToken cancellationToken = default) =>
        GetAsync<Competition>($"competitions/{Uri.EscapeDataString(id.Value)}", cancellationToken);

    /// <summary>Courses come with the map rights work in M4.</summary>
    public Task<Course?> GetCourseAsync(CompetitionId id, string className, CancellationToken cancellationToken = default) =>
        Task.FromResult<Course?>(null);

    /// <summary>Series standings need a data source of their own (SP-03, M3).</summary>
    public Task<Series?> GetSeriesAsync(SeriesId id, CancellationToken cancellationToken = default) =>
        Task.FromResult<Series?>(null);

    public Task<IReadOnlySet<CompetitionId>> GetFavouritesAsync(CancellationToken cancellationToken = default) =>
        _local.GetFavouritesAsync(cancellationToken);

    public Task<bool> ToggleFavouriteAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        _local.ToggleFavouriteAsync(competition, cancellationToken);

    // ---------------------------------------------------------------- IPeopleSource

    public Task<Person> GetMeAsync(CancellationToken cancellationToken = default) =>
        _local.GetMeAsync(cancellationToken);

    public Task<IReadOnlyList<FollowedPerson>> GetMyGroupAsync(CancellationToken cancellationToken = default) =>
        _local.GetMyGroupAsync(cancellationToken);

    public Task<IReadOnlyList<Person>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        _local.SearchAsync(query, cancellationToken);

    public Task FollowAsync(PersonId person, FollowReason reason, CancellationToken cancellationToken = default) =>
        _local.FollowAsync(person, reason, cancellationToken);

    public Task UnfollowAsync(PersonId person, CancellationToken cancellationToken = default) =>
        _local.UnfollowAsync(person, cancellationToken);

    // ---------------------------------------------------------------- IParticipationSource

    /// <summary>My entries need an identified person in Eventor — the auth model is M2/M5.</summary>
    public Task<IReadOnlyList<CompetitionEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompetitionEntry>>([]);

    public Task<IReadOnlyList<Start>> GetStartsAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        ListAsync<Start>($"competitions/{Uri.EscapeDataString(competition.Value)}/starts", cancellationToken);

    public Task<IReadOnlyList<CompetitionResult>> GetResultsAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        ListAsync<CompetitionResult>($"competitions/{Uri.EscapeDataString(competition.Value)}/results", cancellationToken);

    public Task<IReadOnlyList<CompetitionResult>> GetResultsForPersonAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompetitionResult>>([]);

    /// <summary>Prediction is M3, and an unbacktested number is worse than none (SP-11).</summary>
    public Task<Prediction?> GetPredictionAsync(CompetitionId competition, PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult<Prediction?>(null);

    // ---------------------------------------------------------------- ILiveSource

    /// <summary>Live is LiveResults, and matching it to Eventor is SP-04 (M2).</summary>
    public Task<IReadOnlyList<Competition>> GetLiveCompetitionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Competition>>([]);

    public Task<LiveSnapshot> GetSnapshotAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LiveSnapshot
        {
            Competition = competition,
            GeneratedAt = DateTimeOffset.Now,
            Entries = [],
        });

    // ---------------------------------------------------------------- IProgressSource

    /// <summary>Sverigelistan needs a machine-readable source (SP-02, M3).</summary>
    public Task<RankingSnapshot?> GetRankingAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult<RankingSnapshot?>(null);

    public Task<IReadOnlyList<SeriesStanding>> GetSeriesStandingsAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SeriesStanding>>([]);

    // ---------------------------------------------------------------- transport

    private async Task<IReadOnlyList<T>> ListAsync<T>(string path, CancellationToken cancellationToken) =>
        await GetAsync<List<T>>(path, cancellationToken) ?? [];

    /// <summary>
    /// Anything that is not an answer becomes <see cref="SourceUnavailableException"/>, which
    /// is what the offline package listens for. A 404 is an answer: this does not exist.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(path, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return default;

            if (!response.IsSuccessStatusCode)
                throw new SourceUnavailableException($"Orienteras backend svarade {(int)response.StatusCode}.");

            return await response.Content.ReadFromJsonAsync<T>(OrienteraJson.Options, cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or System.Text.Json.JsonException
            // A timeout is the source being unavailable; the caller giving up is not.
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new SourceUnavailableException("Orienteras backend kunde inte nås.");
        }
    }
}
