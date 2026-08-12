using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Activities;

namespace Orientera.Backend.Functions;

/// <summary>
/// The club's activity list: relays to join, trainings, district gatherings.
/// </summary>
/// <remarks>
/// Eventor documents <c>/api/activities</c> and answers 403 for our key, so this reads the page
/// instead. Which club is not a parameter: it is the organisation the backend holds a key for,
/// read as the person it is configured as. There is nothing here to ask for on someone else's
/// behalf.
/// </remarks>
public sealed class ActivityFunctions(
    ClubActivitySource _activities,
    ILogger<ActivityFunctions> _logger)
{
    [Function("GetClubActivities")]
    public Task<IResult> GetClubActivities(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "activities")] HttpRequest request,
        CancellationToken cancellationToken) =>
        Bff.ServeAsync(_logger, () => _activities.ForConfiguredClubAsync(cancellationToken));
}
