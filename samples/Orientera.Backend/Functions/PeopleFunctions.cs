using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Eventor;

namespace Orientera.Backend.Functions;

/// <summary>
/// Finding someone to follow. The answer comes from start and result lists the backend has
/// already fetched — Eventor has no public person lookup, and this needs none.
/// </summary>
public sealed class PeopleFunctions(PeopleSearch _people, ILogger<PeopleFunctions> _logger)
{
    [Function("SearchPeople")]
    public Task<IResult> SearchPeople(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people")] HttpRequest request,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _people.SearchAsync(
            request.Query["q"].FirstOrDefault() ?? string.Empty,
            cancellationToken));
}
