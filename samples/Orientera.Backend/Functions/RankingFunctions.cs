using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Ranking;

namespace Orientera.Backend.Functions;

/// <summary>
/// Sverigelistan for one club, read on demand and cached.
/// </summary>
/// <remarks>
/// This replaces a nightly sweep of all three thousand clubs. The sweep would have built a copy
/// of a list the federation charges for, to answer questions nobody had asked yet; this reads one
/// public page when a runner actually opens the app, and remembers it for half a day.
/// </remarks>
public sealed class RankingFunctions(
    RankingScraper _ranking,
    RunnerRankingSource _runner,
    ILogger<RankingFunctions> _logger)
{
    /// <summary>
    /// One runner's own Sverigelistan, with points per discipline and which results count.
    /// </summary>
    /// <remarks>
    /// Only for members of the organisation whose API key the backend holds — Eventor answers 403
    /// for anyone else, whichever organisation id is passed. That boundary is Eventor's, and this
    /// endpoint inherits it rather than working around it.
    /// </remarks>
    /// <summary>
    /// The ranking for whoever the prototype is configured as. A stand-in for an authenticated
    /// user: the app has no Eventor person id until the identity is a real login (M5, #106).
    /// </summary>
    [Function("GetMyRanking")]
    public Task<IResult> GetMyRanking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ranking/me")] HttpRequest request,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _runner.ForConfiguredPersonAsync(cancellationToken));

    [Function("GetRunnerRanking")]
    public Task<IResult> GetRunnerRanking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ranking/runners/{personId}")] HttpRequest request,
        string personId,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _runner.ForPersonAsync(personId, cancellationToken));

    [Function("GetClubRanking")]
    public Task<IResult> GetClubRanking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ranking/clubs/{clubId}")] HttpRequest request,
        string clubId,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _ranking.ForClubAsync(clubId, cancellationToken));
}
