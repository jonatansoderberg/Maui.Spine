using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>Why a feature does not answer.</summary>
public enum AIOffReason
{
    /// <summary>There is no such feature, its provider has no factory or key, or it has no model.</summary>
    NotConfigured,

    /// <summary><c>Enabled</c> is <see langword="false"/>.</summary>
    Disabled,

    /// <summary>A hard budget is spent: the app's, the feature's or the caller's.</summary>
    Budget,

    /// <summary>The feature answers only signed-in users.</summary>
    SignInRequired,
}

/// <summary>
/// A feature refused a call. <see cref="ToResult"/> gives the answer <c>MapSpineAI</c> gives, so an app's own
/// endpoint can pass it on unchanged.
/// </summary>
/// <example>
/// <code>
/// catch (AIFeatureException e) { return e.ToResult(); }
/// </code>
/// </example>
public abstract class AIFeatureException(string feature, string message) : Exception(message)
{
    /// <summary>The feature that refused.</summary>
    public string Feature { get; } = feature;

    /// <summary>The HTTP answer for the app.</summary>
    public abstract IResult ToResult();
}

/// <summary>The feature is off for this call.</summary>
/// <param name="feature">The feature.</param>
/// <param name="reason">Why.</param>
public sealed class AIFeatureOffException(string feature, AIOffReason reason)
    : AIFeatureException(feature, $"The AI feature '{feature}' is off: {reason}.")
{
    /// <summary>Why the feature is off.</summary>
    public AIOffReason Reason { get; } = reason;

    /// <summary>
    /// 404 with <c>{ "off": "budget" }</c> and the like, the answer an app already treats as "no such feature
    /// right now". 403 when the caller has to sign in.
    /// </summary>
    public override IResult ToResult() => Results.Json(
        new { off = JsonName(Reason) },
        statusCode: Reason == AIOffReason.SignInRequired ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound);

    internal static string JsonName(AIOffReason reason) => reason switch
    {
        AIOffReason.NotConfigured => "notConfigured",
        AIOffReason.Disabled => "disabled",
        AIOffReason.Budget => "budget",
        _ => "signInRequired",
    };
}

/// <summary>The caller has made too many calls, or has too many running.</summary>
/// <param name="feature">The feature.</param>
/// <param name="retryAfter">When a new call may succeed, when the limiter knows.</param>
public sealed class AIRateLimitedException(string feature, TimeSpan? retryAfter)
    : AIFeatureException(feature, $"Too many calls to the AI feature '{feature}'.")
{
    /// <summary>When a new call may succeed, when the limiter knows.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>429, with <c>Retry-After</c> in whole seconds when known.</summary>
    public override IResult ToResult() => new WithHeader(
        Results.Json(new { limited = Feature }, statusCode: StatusCodes.Status429TooManyRequests),
        "Retry-After",
        RetryAfter is { } wait ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture) : null);

    private sealed class WithHeader(IResult inner, string name, string? value) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            if (value is not null) httpContext.Response.Headers[name] = value;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
