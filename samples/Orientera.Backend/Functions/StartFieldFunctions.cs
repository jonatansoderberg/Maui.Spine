using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Eventor;
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
    EntryListSource _entries,
    ILogger<StartFieldFunctions> _logger)
{
    [Function("GetStartField")]
    public Task<IResult> GetStartField(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{competitionId}/field")] HttpRequest request,
        string competitionId,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _field.ForClassAsync(
            competitionId, request.Query["class"].ToString(), cancellationToken));

    /// <summary>
    /// Who has entered, for the stretch before the draw when the start list is empty and this is
    /// the only answer there is to "who else is going?".
    /// </summary>
    [Function("GetEntryList")]
    public Task<IResult> GetEntryList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "competitions/{competitionId}/entries")] HttpRequest request,
        string competitionId,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _entries.ForClassAsync(
            competitionId,
            request.Query["class"].ToString() is { Length: > 0 } className ? className : null,
            cancellationToken));
}
