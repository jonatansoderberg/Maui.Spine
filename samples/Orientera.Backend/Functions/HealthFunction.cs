using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;
using Orientera.Backend.Configuration;

namespace Orientera.Backend.Functions;

/// <summary>
/// Says whether the backend is wired up, without saying anything about the key itself.
/// </summary>
public sealed class HealthFunction(IOptions<EventorOptions> _options)
{
    [Function("GetHealth")]
    public IResult GetHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request) =>
        Results.Json(new
        {
            status = _options.Value.IsConfigured ? "ready" : "unconfigured",
            source = _options.Value.BaseAddress,
        });
}
