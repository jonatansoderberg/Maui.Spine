using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Backend.Eventor;
using Orientera.Domain;

namespace Orientera.Backend.Ranking;

/// <summary>
/// A runner's own Sverigelistan, fetched on their behalf.
/// </summary>
/// <remarks>
/// Sverigelistan's per-runner page is behind the federation's fee and has no API — the ranking
/// area is server-rendered HTML behind a web session, verified in a browser. Eventor does publish
/// a documented way in: <c>externalLoginUrl</c> mints a one-time link that puts a person into
/// logged-in mode, and following it with a cookie jar yields that person's session.
///
/// Two things bound what this can do, and both are enforced by Eventor rather than by us:
/// the link can only be minted for a person who is a member of the organisation whose API key is
/// used — asking for anyone else answers 403, including when their own club id is passed — and
/// the session only ever reaches that one person's own pages.
///
/// So this delivers to a runner what that runner already pays to see, and can reach nobody else's.
/// It is still a server acting as a person, which is why it is scoped to a prototype and why the
/// standing recommendation is an API from the federation (issue #103, #106).
/// </remarks>
public sealed class RunnerRankingSource(
    EventorClient _eventor,
    ResponseCache _cache,
    IOptions<EventorOptions> _options,
    IOptions<RankingOptions> _ranking,
    ILogger<RunnerRankingSource> _logger)
{
    /// <summary>Whoever <c>Ranking:DemoSessionPersonId</c> names, until there is a real login.</summary>
    public Task<RankingSnapshot?> ForConfiguredPersonAsync(CancellationToken cancellationToken) =>
        _ranking.Value.DemoSessionPersonId is { Length: > 0 } person
            ? ForPersonAsync(person, cancellationToken)
            : Task.FromResult<RankingSnapshot?>(null);

    public Task<RankingSnapshot?> ForPersonAsync(string personId, CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"ranking:runner:{personId}",
            TimeSpan.FromHours(12),
            token => FetchAsync(personId, token),
            cancellationToken);

    private async Task<RankingSnapshot?> FetchAsync(string personId, CancellationToken cancellationToken)
    {
        try
        {
            // The runner's own session first. It is the only one that is unambiguously theirs.
            var loginUrl = await LoginUrlAsync(personId, cancellationToken);

            if (loginUrl is null && _ranking.Value.DemoSessionPersonId is { Length: > 0 } demo)
            {
                _logger.LogWarning(
                    "Sverigelistan för {Person} hämtas med {Demo}:s session — prototypläge, se RankingOptions.",
                    personId, demo);

                loginUrl = await LoginUrlAsync(demo, cancellationToken);
            }

            if (loginUrl is null)
                return null;

            // One handler, one cookie jar, one person. A shared client would mean one runner's
            // session answering another runner's request.
            using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            using (var login = await http.GetAsync(loginUrl, cancellationToken))
            {
                if (!login.IsSuccessStatusCode)
                    return null;
            }

            using var page = await http.GetAsync(Url($"Ranking/ol/Runner/Index/{personId}"), cancellationToken);

            if (!page.IsSuccessStatusCode)
                return null;

            var html = await page.Content.ReadAsStringAsync(cancellationToken);
            var snapshot = RunnerRankingParser.Parse(personId, html, DateOnly.FromDateTime(DateTime.Now));

            return snapshot is null ? null : await WithClubPlaceAsync(snapshot, personId, html, http, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Sverigelistan kunde inte hämtas för {Person}.", personId);

            return null;
        }
    }

    /// <summary>
    /// The runner's place inside their own club. The runner page does not state it — it only
    /// links the club — so the club page is read too, and the row with this runner's id carries
    /// the number and which half of the club it counts within.
    /// </summary>
    /// <remarks>
    /// Read through the runner's own session rather than through <see cref="RankingScraper"/>,
    /// which is anonymous. Measured on Gävle OK: anonymously the club page lists one runner and
    /// says why — not logged in, or the club has not paid — and through a member's session it
    /// lists all 188. So this is the same page the runner sees when they open it themselves, and
    /// only their own row is taken from it.
    ///
    /// A club that cannot be read leaves the place absent rather than guessed.
    /// </remarks>
    private async Task<RankingSnapshot> WithClubPlaceAsync(
        RankingSnapshot snapshot,
        string personId,
        string html,
        HttpClient session,
        CancellationToken cancellationToken)
    {
        if (RunnerRankingParser.Club(html) is not { } club)
            return snapshot;

        using var page = await session.GetAsync(Url($"Ranking/ol/Club/Index/{club.Id}"), cancellationToken);

        if (!page.IsSuccessStatusCode)
            return snapshot;

        var rows = RankingPageParser.Parse(club.Id, await page.Content.ReadAsStringAsync(cancellationToken));
        var mine = rows.FirstOrDefault(r => r.RunnerId == personId);

        return mine is null
            ? snapshot
            : snapshot with
            {
                Club = new ClubStanding { Club = club.Name, Place = mine.ClubRank, Section = mine.Section },
            };
    }

    private string Url(string path) => new Uri(new Uri(_ranking.Value.BaseAddress), path).ToString();

    /// <summary>
    /// Asks Eventor for a one-time login link. Answers 403 for anyone outside the organisation
    /// whose key is used, which is the boundary this whole feature lives inside.
    /// </summary>
    private async Task<string?> LoginUrlAsync(string personId, CancellationToken cancellationToken)
    {
        if (_options.Value.OrganisationIds is not { Length: > 0 } organisation)
            return null;

        var response = await _eventor.GetAsync(
            "externalLoginUrl",
            new Dictionary<string, string?>
            {
                ["personId"] = personId,
                ["organisationId"] = organisation.Split(',')[0].Trim(),
            },
            cancellationToken);

        return response is XElement { Value.Length: > 0 } element ? element.Value : null;
    }
}
