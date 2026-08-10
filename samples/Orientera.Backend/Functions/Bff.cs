using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Upstream;
using Orientera.Services.Sources;

namespace Orientera.Backend.Functions;

/// <summary>
/// The two answers the app must be able to tell apart: "there is nothing here" and "the
/// source is down". The first is an empty result, the second a 502 — which is what makes the
/// app's offline package take over instead of showing an empty list as if it were the truth.
/// </summary>
internal static class Bff
{
    public static async Task<IResult> ServeAsync<T>(ILogger logger, Func<Task<T>> load)
    {
        try
        {
            var value = await load();

            return value is null
                ? Results.NotFound()
                : Results.Json(value, OrienteraJson.Options);
        }
        catch (UpstreamUnavailableException exception)
        {
            logger.LogWarning(exception, "Eventor är inte tillgängligt.");

            return Results.Json(
                new { error = "source_unavailable", message = exception.Message },
                OrienteraJson.Options,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
