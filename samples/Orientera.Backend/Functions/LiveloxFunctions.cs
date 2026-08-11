using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Livelox;
using Orientera.Domain;

namespace Orientera.Backend.Functions;

/// <summary>
/// Whether a competition exists in Livelox, and where. A 404 here means Livelox does not have
/// it — an answer, not a failure.
/// </summary>
public sealed class LiveloxFunctions(LiveloxSource _livelox, ILogger<LiveloxFunctions> _logger)
{
    [Function("GetLivelox")]
    public Task<IResult> GetLivelox(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{id}/livelox")] HttpRequest request,
        string id,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _livelox.GetAsync(new CompetitionId(id), cancellationToken));
}
