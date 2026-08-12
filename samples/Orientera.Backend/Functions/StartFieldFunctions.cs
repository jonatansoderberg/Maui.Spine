using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Ranking;

namespace Orientera.Backend.Functions;

/// <summary>
/// The start field of one class, sorted by Sverigelistan.
/// </summary>
/// <remarks>
/// Not a forecast. Three measurements said a placement interval covers half the field to be
/// honest, so this shows what the interval was made of instead: who is running and how the list
/// ranks them (issues #113, #117, #119).
/// </remarks>
public sealed class StartFieldFunctions(
    StartFieldSource _field,
    ILogger<StartFieldFunctions> _logger)
{
    [Function("GetStartField")]
    public Task<IResult> GetStartField(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{competitionId}/field")] HttpRequest request,
        string competitionId,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _field.ForClassAsync(
            competitionId, request.Query["class"].ToString(), cancellationToken));
}
