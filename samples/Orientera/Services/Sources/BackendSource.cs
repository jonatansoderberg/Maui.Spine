using System.Net;
using System.Net.Http.Json;
using Orientera.Domain;
using Orientera.Services.Local;
using Orientera.Services.Offline;

namespace Orientera.Services.Sources;

/// <summary>
/// The app against <c>Orientera.Backend</c>. Competitions, starts and results come from
/// Eventor through the BFF; the rest is answered without inventing anything.
/// </summary>
/// <remarks>
/// Three kinds of data meet here. What is integrated comes over HTTP: competitions, starts and
/// results from Eventor, live from LiveResults. What is local by principle — who I am, who I
/// follow, what I have starred — stays local and keeps working without a connection or an
/// account. What M3 will integrate — my entries, prediction, Sverigelistan — is empty rather
/// than borrowed from the fake dataset: a real calendar next to a fabricated entry would be
/// worse than an honest empty state.
/// </remarks>
public sealed class BackendSource(
    HttpClient _http,
    IOrienteraSource _local,
    LocalIdentityStore _identity,
    LocalGroupStore _group)
    : IOrienteraSource
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

    /// <summary>
    /// Against real data "me" is whoever the user said they are; the seeded runner only stands
    /// in until they have said it, so the screens have something to render.
    /// </summary>
    public async Task<Person> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var seeded = await _local.GetMeAsync(cancellationToken);
        return _identity.AsPerson(seeded) ?? seeded;
    }

    /// <summary>
    /// Min grupp is local, and against a real backend it starts empty: the demo dataset's three
    /// followed runners belong to the demo, not to whoever installed the app.
    /// </summary>
    public Task<IReadOnlyList<FollowedPerson>> GetMyGroupAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_group.All());

    /// <summary>
    /// Real people, from result lists the backend has already fetched. Eventor has no public
    /// person lookup, so this is what a real search can be without new access (SP-04) — and it
    /// is a search over people who exist, which the seeded demo list was not.
    /// </summary>
    public async Task<IReadOnlyList<Person>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        await GetAsync<List<Person>>($"people?q={Uri.EscapeDataString(query)}", cancellationToken) ?? [];

    public Task FollowAsync(Person person, FollowReason reason, CancellationToken cancellationToken = default)
    {
        _group.Follow(person, reason);
        return Task.CompletedTask;
    }

    public Task UnfollowAsync(PersonId person, CancellationToken cancellationToken = default)
    {
        _group.Unfollow(person);
        return Task.CompletedTask;
    }


    // ---------------------------------------------------------------- IParticipationSource

    /// <summary>
    /// My entries need an identified person <em>in Eventor</em>. The local identity names a
    /// runner well enough for live and result lists, but not well enough to claim an entry —
    /// that needs the auth model, which is M5.
    /// </summary>
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

    public Task<IReadOnlyList<Competition>> GetLiveCompetitionsAsync(CancellationToken cancellationToken = default) =>
        ListAsync<Competition>("live", cancellationToken);

    /// <summary>
    /// A competition with no live source resolved answers with an empty field rather than an
    /// error — there is nothing wrong, there is simply nothing to follow.
    /// </summary>
    public async Task<LiveSnapshot> GetSnapshotAsync(
        CompetitionId competition,
        string? className = null,
        CancellationToken cancellationToken = default)
    {
        var path = $"competitions/{Uri.EscapeDataString(competition.Value)}/live";

        if (className is not null)
            path += $"?class={Uri.EscapeDataString(className)}";

        return await GetAsync<LiveSnapshot>(path, cancellationToken)
            ?? new LiveSnapshot
            {
                Competition = competition,
                GeneratedAt = DateTimeOffset.Now,
                Entries = [],
            };
    }

    // ---------------------------------------------------------------- ILiveloxSource

    /// <summary>
    /// Where this competition lives in Livelox, if it does. A link, not data: maps and routes are
    /// Livelox's to show, and the course endpoint needs a scope our key does not carry.
    /// </summary>
    public Task<LiveloxLink?> GetLiveloxAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        GetAsync<LiveloxLink>($"competitions/{Uri.EscapeDataString(competition.Value)}/livelox", cancellationToken);

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
