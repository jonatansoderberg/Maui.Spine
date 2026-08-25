using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Arena;
using Orientera.Backend.Eventor;
using Orientera.Domain;

namespace Orientera.Backend.Functions;

/// <summary>
/// Tävlingens arenabild, om den hunnit bli till.
/// </summary>
/// <remarks>
/// 404 betyder här "inte genererad än", inte "finns inte" — beställningen läggs vid samma
/// uppslag. Appen ska därför visa sitt vanliga kort utan bild och försöka igen senare, inte
/// behandla det som ett fel.
/// </remarks>
public sealed class ArenaImageFunctions(
    EventorSource _source,
    ArenaImageStore _store,
    ILogger<ArenaImageFunctions> _logger)
{
    [Function("GetArenaImage")]
    public Task<IResult> GetArenaImage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{id}/arenabild")]
        HttpRequest request,
        string id,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, async () =>
        {
            var competition = await _source.GetCompetitionAsync(new CompetitionId(id), cancellationToken);

            return competition is null
                ? null
                : await _store.FindAsync(competition, cancellationToken);
        });
}
