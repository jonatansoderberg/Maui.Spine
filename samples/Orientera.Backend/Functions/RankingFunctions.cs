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
public sealed class RankingFunctions(RankingScraper _ranking, ILogger<RankingFunctions> _logger)
{
    [Function("GetClubRanking")]
    public Task<IResult> GetClubRanking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ranking/clubs/{clubId}")] HttpRequest request,
        string clubId,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _ranking.ForClubAsync(clubId, cancellationToken));
}
