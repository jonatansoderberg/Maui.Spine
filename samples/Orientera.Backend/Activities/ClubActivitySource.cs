using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Backend.Eventor;
using Orientera.Domain;

namespace Orientera.Backend.Activities;

/// <summary>
/// A club's activity list — the relays, trainings and gatherings its members sign up for.
/// </summary>
/// <remarks>
/// Anonymously the page answers with nothing at all, so this needs a member's session. That is the
/// same mechanism and the same setting as Sverigelistan, but a milder version of the same
/// question: the list is the club's own, and it is read for a member of that club.
/// </remarks>
public sealed class ClubActivitySource(
    EventorSession _sessions,
    ResponseCache _cache,
    IOptions<EventorOptions> _eventor,
    IOptions<RankingOptions> _ranking,
    ILogger<ClubActivitySource> _logger)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(_eventor.Value.TimeZone);

    /// <summary>
    /// The activities of the organisation whose key the backend holds, read as whoever
    /// <c>Ranking:DemoSessionPersonId</c> names. Empty until that is set, because there is no
    /// logged-in user to be yet (M5, #106).
    /// </summary>
    public Task<IReadOnlyList<ClubActivity>> ForConfiguredClubAsync(CancellationToken cancellationToken) =>
        _ranking.Value.DemoSessionPersonId is { Length: > 0 } person
            ? ForOrganisationAsync(person, Organisation(), cancellationToken)
            : Task.FromResult<IReadOnlyList<ClubActivity>>([]);

    public Task<IReadOnlyList<ClubActivity>> ForOrganisationAsync(
        string personId, string? organisationId, CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"activities:{organisationId}",
            // Sign-ups arrive over days, and a deadline that moves is the organiser changing it,
            // not the hour passing. An hour is short enough to notice either.
            TimeSpan.FromHours(1),
            token => FetchAsync(personId, organisationId, token),
            cancellationToken);

    private async Task<IReadOnlyList<ClubActivity>> FetchAsync(
        string personId, string? organisationId, CancellationToken cancellationToken)
    {
        if (organisationId is null)
            return [];

        try
        {
            using var session = await _sessions.OpenAsync(personId, cancellationToken);

            if (session is null)
                return [];

            using var page = await session.GetAsync(
                $"https://eventor.orientering.se/Activities?organisationId={organisationId}",
                cancellationToken);

            if (!page.IsSuccessStatusCode)
                return [];

            return ActivityPageParser.Parse(
                await page.Content.ReadAsStringAsync(cancellationToken), _zone);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Klubbaktiviteterna kunde inte hämtas för {Organisation}.", organisationId);

            return [];
        }
    }

    private string? Organisation() =>
        _eventor.Value.OrganisationIds is { Length: > 0 } ids ? ids.Split(',')[0].Trim() : null;
}
