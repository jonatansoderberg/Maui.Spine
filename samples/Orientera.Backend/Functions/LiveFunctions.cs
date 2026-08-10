using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.LiveResults;
using Orientera.Domain;

namespace Orientera.Backend.Functions;

/// <summary>
/// Live over the BFF. The app asks by Eventor competition and never learns that LiveResults
/// exists — which is what lets the match be corrected, or the source replaced, without
/// touching a phone.
/// </summary>
public sealed class LiveFunctions(LiveSource _live, ILogger<LiveFunctions> _logger)
{
    [Function("GetLiveCompetitions")]
    public Task<IResult> GetLiveCompetitions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "live")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.TryParse(request.Query["date"], out var date)
            ? date
            : DateOnly.FromDateTime(DateTime.Now);

        return Bff.ServeAsync(_logger, () => _live.GetLiveCompetitionsAsync(today, cancellationToken));
    }

    [Function("GetLiveSnapshot")]
    public Task<IResult> GetLiveSnapshot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{id}/live")] HttpRequest request,
        string id,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _live.GetSnapshotAsync(
            new CompetitionId(id),
            request.Query["class"].FirstOrDefault(),
            cancellationToken));

    /// <summary>
    /// What the backend believes the live source for a competition is, and how sure it is.
    /// A match is a guess made on a runner's behalf, so it has to be inspectable (SP-04).
    /// </summary>
    [Function("GetLiveMatch")]
    public Task<IResult> GetLiveMatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{id}/live/match")] HttpRequest request,
        string id,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _live.MatchAsync(new CompetitionId(id), cancellationToken));
}
