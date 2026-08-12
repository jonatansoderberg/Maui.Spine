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
///
/// A runner's own page is not here at all. It is behind Sverigelistan's fee, and since #123 it is
/// read on the runner's own phone with the login they made themselves — a server that reads it on
/// their behalf is one member's subscription answering for everybody.
/// </remarks>
public sealed class RankingFunctions(
    RankingScraper _ranking,
    ILogger<RankingFunctions> _logger)
{
    [Function("GetClubRanking")]
    public Task<IResult> GetClubRanking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ranking/clubs/{clubId}")] HttpRequest request,
        string clubId,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _ranking.ForClubAsync(clubId, cancellationToken));
}
