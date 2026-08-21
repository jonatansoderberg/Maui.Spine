using System.Net;
using System.Net.Http.Json;
using Orientera.Domain;
using Orientera.Services.Eventor;
using Orientera.Services.Local;
using Orientera.Services.Offline;

namespace Orientera.Services.Sources;

/// <summary>
/// The app against <c>Orientera.Backend</c>. Competitions, starts and results come from
/// Eventor through the BFF; the rest is answered without inventing anything.
/// </summary>
/// <remarks>
/// Four kinds of data meet here. What is integrated comes over HTTP: competitions, starts and
/// results from Eventor, live from LiveResults. What is local by principle — who I am, who I
/// follow, what I am interested in — stays local and keeps working without a connection or an
/// account. What sits behind the reader's own Eventor login — Sverigelistan, the club's
/// activities, the points beside a start field — is read on the phone through
/// <see cref="EventorReader"/> and is empty until they log in (#123). What is still unintegrated
/// is empty rather than borrowed from the fake dataset: a real calendar next to a fabricated entry
/// would be worse than an honest empty state.
/// </remarks>
public sealed class BackendSource(
    HttpClient _http,
    IOrienteraSource _local,
    LocalIdentityStore _identity,
    LocalGroupStore _group,
    EventorReader _eventor)
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

    public Task<IReadOnlySet<CompetitionId>> GetInterestsAsync(CancellationToken cancellationToken = default) =>
        _local.GetInterestsAsync(cancellationToken);

    public Task<bool> ToggleInterestAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        _local.ToggleInterestAsync(competition, cancellationToken);

    // ---------------------------------------------------------------- IPeopleSource

    /// <summary>
    /// Who the app is reading as, carrying Eventor's own person id once there is a login.
    /// </summary>
    /// <remarks>
    /// The id matters and is not cosmetic. Start lists and results come from the backend with
    /// Eventor's <c>PersonId</c> on every row, while an identity typed in by hand can only be
    /// <c>me:name-club</c> — so "min starttid" compared two id spaces that could never meet and
    /// found nothing, on a page that was showing the runner's own start list at the time.
    /// </remarks>
    public async Task<Person> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var seeded = await _local.GetMeAsync(cancellationToken);
        var me = _identity.AsPerson(seeded) ?? seeded;

        return await _eventor.StartPageAsync(cancellationToken) is { PersonId: { Length: > 0 } id }
            ? me with { Id = new PersonId(id) }
            : me;
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
    /// The reader's own entries, from Eventor's "Mina tävlingar" on this phone.
    /// </summary>
    /// <remarks>
    /// Empty until they log in, and empty is honest: an app that cannot see the entry says
    /// "Anmälan öppen", which is what a competition looks like to somebody who has not entered.
    /// It also costs more than a badge — <c>MyEntries</c> is the heaviest single signal in
    /// <see cref="Relevance.RelevanceEngine"/>, so without it "För dig" ranks on size and distance
    /// alone and puts a championship two districts away above the race you are running on Sunday.
    /// </remarks>
    public async Task<IReadOnlyList<CompetitionEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var me = await GetMeAsync(cancellationToken);

        return
        [
            .. (await _eventor.EntriesAsync(cancellationToken)).Select(entry => new CompetitionEntry
            {
                Competition = new CompetitionId(entry.EventId),
                Person = me.Id,
                Class = entry.Class,

                // Eventor's page does not say when the entry was made, and the app only ever asks
                // whether it is in the past. Any real moment would be a guess; this one is not
                // mistakable for a fact.
                RegisteredAt = DateTimeOffset.MinValue,
            }),
        ];
    }

    public Task<IReadOnlyList<Start>> GetStartsAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        ListAsync<Start>($"competitions/{Uri.EscapeDataString(competition.Value)}/starts", cancellationToken);

    public Task<IReadOnlyList<CompetitionResult>> GetResultsAsync(
        CompetitionId competition, CancellationToken cancellationToken = default) =>
        ListAsync<CompetitionResult>(
            $"competitions/{Uri.EscapeDataString(competition.Value)}/results", cancellationToken);

    /// <summary>
    /// The reader's own rows in a set of result lists, fetched as exactly that.
    /// </summary>
    /// <remarks>
    /// One request for a whole season. Eventor answers a person and a list of events directly,
    /// so the app no longer pulls a competition at a time to find one row in it.
    /// </remarks>
    public Task<IReadOnlyList<CompetitionResult>> GetOwnResultsAsync(
        PersonId person, IReadOnlyList<CompetitionId> competitions, bool splits = false, CancellationToken cancellationToken = default) =>
        competitions.Count == 0
            ? Task.FromResult<IReadOnlyList<CompetitionResult>>([])
            : ListAsync<CompetitionResult>(
                $"results/person?person={Uri.EscapeDataString(person.Value)}"
                    + $"&events={Uri.EscapeDataString(string.Join(',', competitions.Select(c => c.Value).Distinct()))}"
                    + (splits ? "&splits=true" : string.Empty),
                cancellationToken);

    /// <summary>
    /// The reader's own season, from Eventor's "Mina tävlingar" on this phone.
    /// </summary>
    /// <remarks>
    /// Not from the backend, and not assembled from result lists: a result list says who ran a
    /// competition, never which competitions you ran. Only the reader's own page knows that, and
    /// it is behind their login — so it is read here, like the ranking and the entries.
    ///
    /// The rows carry their own name and date. The calendar reaches a few months back and these
    /// go to January, so a result cannot borrow them from a competition the app has in hand.
    /// </remarks>
    public async Task<IReadOnlyList<CompetitionResult>> GetResultsForPersonAsync(
        PersonId person, CancellationToken cancellationToken = default)
    {
        var me = await GetMeAsync(cancellationToken);

        return
        [
            .. (await _eventor.ResultsAsync(cancellationToken))
                .OrderByDescending(r => r.Date)
                .Select(r => new CompetitionResult
                {
                    Id = new ResultId($"{r.EventId}:{me.Id.Value}"),
                    Competition = new CompetitionId(r.EventId),
                    Person = me.Id,
                    Name = me.Name,
                    Club = me.Club,
                    Class = r.Class,

                    // A row without a placement is a race that was started and not finished in a
                    // classifiable way. Eventor's page says "ej godkänd" without saying which of
                    // the reasons it was, so this stops at the one thing it does say.
                    Status = r.Place is null ? ResultStatus.Mispunch : ResultStatus.Ok,
                    Place = r.Place,
                    Time = r.Time,
                    BehindWinner = r.Behind,
                    CompetitionName = r.Name,
                    CompetitionDate = r.Date,
                    CompetitionDiscipline = r.Discipline,
                }),
        ];
    }

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

    /// <summary>
    /// Sverigelistan has no machine-readable source — SP-02 looked, and none of Eventor's
    /// thirty-seven endpoints is the ranking (<c>issues/103-sp02-sverigelistan.md</c>). So it is
    /// read as HTML, and since #123 it is read here, on the phone, with the login the user made
    /// themselves. The person is the phone's own; there is no other ranking to ask for.
    /// </summary>
    public Task<RankingSnapshot?> GetRankingAsync(PersonId person, CancellationToken cancellationToken = default) =>
        _eventor.RankingAsync(cancellationToken);

    public Task<IReadOnlyList<SeriesStanding>> GetSeriesStandingsAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SeriesStanding>>([]);

    // ---------------------------------------------------------------- IClubActivitySource

    /// <summary>
    /// The club's own activity list, read as a member of it — which is now the member holding the
    /// phone rather than whoever the backend was configured as (#123). Logged out there is no club
    /// to read for, and the list is empty rather than someone else's.
    /// </summary>
    public Task<IReadOnlyList<ClubActivity>> GetClubActivitiesAsync(CancellationToken cancellationToken = default) =>
        _eventor.ActivitiesAsync(cancellationToken);

    // ---------------------------------------------------------------- IStartFieldSource

    /// <summary>
    /// Who is entered, from the backend; what Sverigelistan says about them, from the phone.
    /// </summary>
    /// <remarks>
    /// The two halves have different owners. The entries are open data behind the club's API key;
    /// the points sit behind a personal subscription, and reading them for a whole field is only
    /// defensible when it is the reader's own. Without a login the field still stands — in start
    /// order, without points — because who is running is worth knowing on its own.
    /// </remarks>
    /// <summary>
    /// The entry list, plain. No club ids on Eventor's page, so no points can be looked up for it
    /// — and that is the honest shape of a field nobody has drawn yet.
    /// </summary>
    public Task<IReadOnlyList<StartFieldRunner>> GetEntryListAsync(
        CompetitionId competition, string className, CancellationToken cancellationToken = default) =>
        ListAsync<StartFieldRunner>(
            $"competitions/{Uri.EscapeDataString(competition.Value)}/entries?class={Uri.EscapeDataString(className)}",
            cancellationToken);

    public async Task<IReadOnlyList<StartFieldRunner>> GetStartFieldAsync(
        CompetitionId competition, string className, CancellationToken cancellationToken = default)
    {
        var field = await ListAsync<StartFieldRunner>(
            $"competitions/{Uri.EscapeDataString(competition.Value)}/field?class={Uri.EscapeDataString(className)}",
            cancellationToken);

        if (field.Count == 0)
            return field;

        var ranking = await _eventor.ClubRankingAsync(
            field.Select(r => r.ClubId).OfType<string>(), cancellationToken);

        if (ranking.Count == 0)
            return field;

        return
        [
            .. field
                .Select(runner => ranking.TryGetValue(runner.Person.Value, out var row)
                    ? runner with { Points = row.Points, NationalRank = row.NationalRank }
                    : runner)
                // Lower points is a better runner. Whoever the list does not carry goes last, in
                // start order, rather than being given a place they have not earned.
                .OrderBy(r => r.Points ?? double.MaxValue)
                .ThenBy(r => r.StartTime ?? DateTimeOffset.MaxValue),
        ];
    }

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
