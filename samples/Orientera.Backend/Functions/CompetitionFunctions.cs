using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Eventor;
using Orientera.Domain;

namespace Orientera.Backend.Functions;

/// <summary>
/// The BFF surface: normalised competitions, starts and results. No Eventor concept crosses
/// this boundary — the app only ever sees the domain model.
/// </summary>
public sealed class CompetitionFunctions(EventorSource _source, ILogger<CompetitionFunctions> _logger)
{
    [Function("GetCompetitions")]
    public Task<IResult> GetCompetitions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var from = Date(request, "from");
        var to = Date(request, "to");

        return Bff.ServeAsync(_logger, () => _source.GetCompetitionsAsync(from, to, cancellationToken));
    }

    [Function("GetCompetition")]
    public Task<IResult> GetCompetition(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{id}")] HttpRequest request,
        string id,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _source.GetCompetitionAsync(new CompetitionId(id), cancellationToken));

    [Function("GetStarts")]
    public Task<IResult> GetStarts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{id}/starts")] HttpRequest request,
        string id,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _source.GetStartsAsync(new CompetitionId(id), cancellationToken));

    [Function("GetResults")]
    public Task<IResult> GetResults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{id}/results")] HttpRequest request,
        string id,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _source.GetResultsAsync(new CompetitionId(id), cancellationToken));

    private static DateOnly? Date(HttpRequest request, string name) =>
        DateOnly.TryParse(request.Query[name], out var date) ? date : null;
}
