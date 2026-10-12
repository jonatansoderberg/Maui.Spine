using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>The HTTP answers for a refused call: the ones <c>MapSpineAI</c> gives, and the app's <c>Plugin.Maui.Spine.AI</c> reads.</summary>
public static class AIFeatureResults
{
    /// <summary>
    /// The answer for <paramref name="exception"/>, so an app's own endpoint can pass a refusal on unchanged:
    /// <list type="bullet">
    /// <item>404 with <c>{ "off": "budget" }</c> and the like, the answer an app treats as "no such feature right now"</item>
    /// <item>403 with <c>{ "off": "signInRequired" }</c></item>
    /// <item>429 with <c>Retry-After</c> in whole seconds when known</item>
    /// <item>413 with <c>{ "tooLarge": "turns", "max": 20 }</c></item>
    /// </list>
    /// </summary>
    /// <example>
    /// <code>
    /// catch (AIFeatureException e) { return e.ToResult(); }
    /// </code>
    /// </example>
    public static IResult ToResult(this AIFeatureException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            AIFeatureOffException off => Results.Json(
                new { off = AIOffReasons.ToJson(off.Reason) },
                statusCode: off.Reason == AIOffReason.SignInRequired ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound),

            AIRateLimitedException limited => new WithHeader(
                Results.Json(new { limited = limited.Feature }, statusCode: StatusCodes.Status429TooManyRequests),
                "Retry-After",
                limited.RetryAfter is { } wait ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture) : null),

            AIRequestTooLargeException large => Results.Json(
                new { tooLarge = large.Limit, max = large.Max }, statusCode: StatusCodes.Status413PayloadTooLarge),

            _ => Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private sealed class WithHeader(IResult inner, string name, string? value) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            if (value is not null) httpContext.Response.Headers[name] = value;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
