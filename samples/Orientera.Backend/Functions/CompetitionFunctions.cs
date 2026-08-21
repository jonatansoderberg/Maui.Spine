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

    /// <summary>
    /// A competition's whole result list.
    /// </summary>
    /// <remarks>
    /// Whole, because Eventor offers nothing narrower for an ordinary event: <c>results/event</c>
    /// takes no class, and <c>wrsresults/event</c> — which does — answers 404 for anything outside
    /// the world ranking. A caller who only wants its own row asks <c>results/person</c> instead.
    /// </remarks>
    [Function("GetResults")]
    public Task<IResult> GetResults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{id}/results")] HttpRequest request,
        string id,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _source.GetResultsAsync(new CompetitionId(id), cancellationToken));

    /// <summary>
    /// One person's own results in a list of events — the narrow question behind "how large was
    /// the field", answered for a whole season in one request.
    /// </summary>
    [Function("GetPersonResults")]
    public Task<IResult> GetPersonResults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "results/person")] HttpRequest request,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _source.GetPersonResultsAsync(
            request.Query["person"].ToString(),
            [.. request.Query["events"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => new CompetitionId(value))],
            int.TryParse(request.Query["top"], out int top) ? top : null,
            bool.TryParse(request.Query["splits"], out bool splits) && splits,
            cancellationToken));

    private static DateOnly? Date(HttpRequest request, string name) =>
        DateOnly.TryParse(request.Query[name], out var date) ? date : null;
}
