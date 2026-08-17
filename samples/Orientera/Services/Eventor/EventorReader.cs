using Orientera.Domain;
using Orientera.Domain.Activities;
using Orientera.Domain.Eventor;
using Orientera.Domain.Ranking;

namespace Orientera.Services.Eventor;

/// <summary>What the app is allowed to read right now, and why not when it is not.</summary>
public enum EventorAccess
{
    /// <summary>Nobody has logged in on this phone.</summary>
    NoSession,

    /// <summary>There is a stored session and Eventor no longer recognises it.</summary>
    Expired,

    /// <summary>Logged in, but the club has not paid for Sverigelistan this season.</summary>
    NoSubscription,

    /// <summary>Logged in, with Sverigelistan.</summary>
    Available,

    /// <summary>Eventor could not be reached. Says nothing about the session.</summary>
    Unreachable,
}

/// <summary>
/// Reads Eventor on the phone, with the session the user logged in with.
/// </summary>
/// <remarks>
/// This is the whole point of #123. The backend used to read these pages as one configured person,
/// which meant one member's subscription answered for everybody; here each phone reads its own, and
/// the question of who is paying answers itself.
///
/// Nothing is invented when a page is missing. The three ways this comes up empty — no login, a
/// session Eventor has forgotten, and a club without the fee — are told apart by
/// <see cref="EventorAccess"/> and explained separately, because "we do not know" and "you do not
/// have it" are different sentences.
/// </remarks>
public sealed class EventorReader(HttpClient _http, EventorSessionStore _sessions)
{
    private static readonly TimeZoneInfo Sweden = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    private readonly PageCache _cache = new();

    /// <summary>
    /// The start page, which is the liveness check. Five minutes because it is asked before every
    /// other fetch and its answer only changes when the user logs in or Eventor forgets them.
    /// </summary>
    public Task<EventorStartPage?> StartPageAsync(CancellationToken cancellationToken = default) =>
        _cache.GetOrAddAsync(
            "home",
            TimeSpan.FromMinutes(5),
            async token => await GetAsync("Home/Index", token) is { } html
                ? StartPageParser.Parse(html)
                : null,
            cancellationToken);

    public async Task<EventorAccess> AccessAsync(CancellationToken cancellationToken = default)
    {
        if (_sessions.Load() is null)
            return EventorAccess.NoSession;

        return await StartPageAsync(cancellationToken) switch
        {
            null => EventorAccess.Unreachable,
            { IsLoggedIn: false } => EventorAccess.Expired,
            { HasRanking: false } => EventorAccess.NoSubscription,
            _ => EventorAccess.Available,
        };
    }

    /// <summary>
    /// Who the session belongs to, read once at login. The start page carries the name, the id and
    /// the club; the settings page carries the club again — without depending on the ranking fee —
    /// and the class the runner normally enters.
    /// </summary>
    public async Task<EventorAccount?> ReadAccountAsync(CancellationToken cancellationToken = default)
    {
        if (await StartPageAsync(cancellationToken) is not { IsLoggedIn: true } page)
            return null;

        var settings = await GetAsync("MyPages/Settings", cancellationToken) is { } html
            ? SettingsPageParser.Parse(html)
            : new EventorSettings();

        return new EventorAccount
        {
            Name = page.Name!,
            Club = settings.Club ?? page.Club ?? string.Empty,
            ClubId = page.ClubId ?? settings.ClubId,
            DefaultClass = settings.DefaultClass,
        };
    }

    /// <summary>
    /// The reader's own Sverigelistan. Half a day, which is how often Eventor recomputes it.
    /// </summary>
    public async Task<RankingSnapshot?> RankingAsync(CancellationToken cancellationToken = default)
    {
        if (await StartPageAsync(cancellationToken) is not { HasRanking: true } page)
            return null;

        return await _cache.GetOrAddAsync(
            $"ranking:{page.PersonId}",
            TimeSpan.FromHours(12),
            token => FetchRankingAsync(page.PersonId!, token),
            cancellationToken);
    }

    private async Task<RankingSnapshot?> FetchRankingAsync(string personId, CancellationToken cancellationToken)
    {
        if (await GetAsync($"Ranking/ol/Runner/Index/{personId}", cancellationToken) is not { } html)
            return null;

        var snapshot = RunnerRankingParser.Parse(personId, html, DateOnly.FromDateTime(DateTime.Now));

        if (snapshot is null || RunnerRankingParser.Club(html) is not { } club)
            return snapshot;

        // The runner page links the club but never states the place inside it, so the club page is
        // read too and only this runner's row is taken from it.
        var mine = (await ClubAsync(club.Id, cancellationToken)).FirstOrDefault(r => r.RunnerId == personId);

        return mine is null
            ? snapshot
            : snapshot with
            {
                Club = new ClubStanding { Club = club.Name, Place = mine.ClubRank, Section = mine.Section },
            };
    }

    /// <summary>
    /// The club's own activity list. Read as a member of that club, which is what it always was —
    /// only now it is a member who is actually holding the phone.
    /// </summary>
    public async Task<IReadOnlyList<ClubActivity>> ActivitiesAsync(CancellationToken cancellationToken = default)
    {
        if (ClubIdOf(await StartPageAsync(cancellationToken)) is not { } club)
            return [];

        return await _cache.GetOrAddAsync(
            $"activities:{club}",
            // Sign-ups arrive over days, and a deadline that moves is the organiser changing it.
            TimeSpan.FromHours(1),
            async token => await GetAsync($"Activities?organisationId={club}", token) is { } html
                ? ActivityPageParser.Parse(html, Sweden)
                : null,
            cancellationToken) ?? [];
    }

    /// <summary>
    /// The competitions the reader is entered in and has not run yet.
    /// </summary>
    /// <remarks>
    /// On the phone, with the reader's own login, for the same reason as the ranking: an entry is
    /// the most personal thing the app shows, and the backend has no business holding a list of
    /// who is going where. Five minutes, because an entry made on the way to the car should be
    /// visible when the app is next opened — and because the page is the one the user just left.
    /// </remarks>
    public async Task<IReadOnlyList<EventorEntry>> EntriesAsync(CancellationToken cancellationToken = default)
    {
        if (await StartPageAsync(cancellationToken) is not { IsLoggedIn: true })
            return [];

        return await _cache.GetOrAddAsync(
            "my-entries",
            TimeSpan.FromMinutes(5),
            async token => await GetAsync("MyPages/Events", token) is { } html
                ? MyEventsPageParser.Parse(html, DateOnly.FromDateTime(DateTime.Now))
                : null,
            cancellationToken) ?? [];
    }

    /// <summary>
    /// The races the reader has already run this season, off the same page as the entries.
    /// </summary>
    /// <remarks>
    /// Eventor has no "my results" anywhere else: the result list of a competition knows the field
    /// and the calendar knows the race, but only this page knows which of them were yours. Half an
    /// hour, because a result appears once and then stops changing.
    /// </remarks>
    public async Task<IReadOnlyList<EventorResult>> ResultsAsync(CancellationToken cancellationToken = default)
    {
        if (await StartPageAsync(cancellationToken) is not { IsLoggedIn: true })
            return [];

        return await _cache.GetOrAddAsync(
            "my-results",
            TimeSpan.FromMinutes(30),
            async token => await GetAsync("MyPages/Events", token) is { } html
                ? MyEventsPageParser.ParseResults(html, DateOnly.FromDateTime(DateTime.Now))
                : null,
            cancellationToken) ?? [];
    }

    /// <summary>
    /// Sverigelistan for everyone in the given clubs, which is how a start field gets its points.
    /// One page per club, not one per runner: a field of forty spans a dozen clubs.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, RankingRow>> ClubRankingAsync(
        IEnumerable<string> clubIds, CancellationToken cancellationToken = default)
    {
        var rows = new Dictionary<string, RankingRow>(StringComparer.Ordinal);

        if (await StartPageAsync(cancellationToken) is not { HasRanking: true })
            return rows;

        // Four at a time: one page took about a second, and a dozen in turn is longer than anyone
        // waits for a start list. Four is also a polite number to send Eventor at once.
        var pages = new List<IReadOnlyList<RankingRow>>();

        await Parallel.ForEachAsync(
            clubIds.Distinct(StringComparer.Ordinal),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (club, token) =>
            {
                var page = await ClubAsync(club, token);

                lock (pages)
                    pages.Add(page);
            });

        foreach (var row in pages.SelectMany(p => p))
            rows.TryAdd(row.RunnerId, row);

        return rows;
    }

    private async Task<IReadOnlyList<RankingRow>> ClubAsync(string clubId, CancellationToken cancellationToken) =>
        await _cache.GetOrAddAsync(
            $"club:{clubId}",
            TimeSpan.FromHours(12),
            async token => await GetAsync($"Ranking/ol/Club/Index/{clubId}", token) is { } html
                ? RankingPageParser.Parse(clubId, html)
                : null,
            cancellationToken) ?? [];

    /// <summary>
    /// The club id, which the start page only carries inside the ranking box. A club without
    /// Sverigelistan has no box, and then the one the login wrote down stands.
    /// </summary>
    private string? ClubIdOf(EventorStartPage? page) =>
        page is { IsLoggedIn: true }
            ? page.ClubId ?? _sessions.Load()?.Account?.ClubId
            : null;

    /// <summary>
    /// One page, with the session's cookies. Anything that is not an answer is null: Eventor is
    /// read over whatever network a phone happens to have, and an outage is not news.
    /// </summary>
    /// <summary>
    /// What a redirect to the login hands back: a page with nothing on it. Every parser then reads
    /// what is true — no greeting, no ranking box, no rows — and <see cref="AccessAsync"/> reaches
    /// <see cref="EventorAccess.Expired"/> through the branch it already had.
    /// </summary>
    private const string LoginRedirect = "";

    private static bool IsRedirect(System.Net.HttpStatusCode status) =>
        (int)status is >= 300 and < 400;

    private async Task<string?> GetAsync(string path, CancellationToken cancellationToken)
    {
        if (_sessions.Load() is not { } session)
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{EventorSite.Origin}/{path}");
            request.Headers.Add("Cookie", session.Header);

            using var response = await _http.SendAsync(request, cancellationToken);

            // A redirect is Eventor saying the session is not logged in, and it says it in a way
            // that has to be caught here. Measured on #123 after a session died: /Home/Index sends
            // a dead session to /PersistentLogin, which sends it straight back, forever. Following
            // redirects turns that into an exception, an exception reads as "Eventor is down", and
            // the reader is told to wait when they should log in again — the exact collapse of the
            // three empty cases into one shrug that this page set out to avoid.
            if (IsRedirect(response.StatusCode))
                return LoginRedirect;

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Forgets everything read with the old session.</summary>
    public void Clear() => _cache.Clear();
}

/// <summary>
/// Pages already read, until they go stale.
/// </summary>
/// <remarks>
/// The backend has <c>ResponseCache</c> for the same reason; this is its small cousin, and it is
/// here because the phone now makes the requests the backend used to. Without it, opening the Jag
/// tab twice is two quarter-megabyte fetches over mobile data.
/// </remarks>
internal sealed class PageCache
{
    private readonly Dictionary<string, (DateTimeOffset Until, object? Value)> _entries = [];
    private readonly Lock _gate = new();

    public async Task<T?> GetOrAddAsync<T>(
        string key, TimeSpan lifetime, Func<CancellationToken, Task<T?>> read, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached) && cached.Until > DateTimeOffset.UtcNow)
                return (T?)cached.Value;
        }

        var value = await read(cancellationToken);

        // A failed read is not cached: the next attempt should be allowed to succeed.
        if (value is null)
            return value;

        lock (_gate)
            _entries[key] = (DateTimeOffset.UtcNow + lifetime, value);

        return value;
    }

    public void Clear()
    {
        lock (_gate)
            _entries.Clear();
    }
}
