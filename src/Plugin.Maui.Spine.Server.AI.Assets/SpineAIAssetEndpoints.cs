using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>
/// The assets endpoint, in a form both hosts can use: ASP.NET routing calls it through
/// <see cref="SpineAIAssetEndpointRouteBuilderExtensions.MapSpineAIAssets"/>, and an Azure Functions isolated worker
/// calls it from its own <c>[Function]</c>.
/// </summary>
public static class SpineAIAssetEndpoints
{
    /// <summary>Looks up an image of an exposed asset feature for the caller of <paramref name="request"/>.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="feature">The feature's name, from the route.</param>
    /// <param name="key">The key's text, from the route.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The answer; see <see cref="GeneratedAssetResults.ToResult"/>.</returns>
    /// <remarks>Only features with <c>Asset:Expose</c> answer. Any other name gets the same 404 as a feature that is off.</remarks>
    public static async Task<IResult> GetAsync(HttpRequest request, string feature, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(key);

        var services = request.HttpContext.RequestServices;
        var assets = services.GetRequiredService<GeneratedAssets>();
        if (!assets.IsExposed(feature))
            return new AIFeatureOffException(feature, AIOffReason.NotConfigured).ToResult();

        var caller = await services.GetRequiredService<IAIFeatures>().IdentifyAsync(request);
        var asset = await assets.LookUpAsync(feature, key, caller, cancellationToken);
        return asset.ToResult(feature, assets.Time);
    }
}

/// <summary>The HTTP answers for a lookup, for an app's own endpoint.</summary>
public static class GeneratedAssetResults
{
    /// <summary>
    /// The answer for <paramref name="asset"/>:
    /// <list type="bullet">
    /// <item>200 <c>{ "status": "ready", "url": "…?v=…" }</c></item>
    /// <item>202 <c>{ "status": "pending" }</c> with <c>Retry-After</c></item>
    /// <item>502 <c>{ "status": "failed", "reason": "check" }</c> with <c>Retry-After</c>: when the image is ordered again</item>
    /// <item>404 <c>{ "status": "notFound" }</c> for an unknown key, and Server.AI's 404 <c>{ "off": "budget" }</c> and the like when the feature is off</item>
    /// <item>429 with <c>Retry-After</c> when the caller ordered too many</item>
    /// </list>
    /// </summary>
    /// <param name="asset">The lookup's answer.</param>
    /// <param name="feature">The feature's name, for the off and rate-limited answers.</param>
    /// <param name="time">The clock for <c>Retry-After</c>.</param>
    public static IResult ToResult(this GeneratedAsset asset, string feature, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var now = (time ?? TimeProvider.System).GetUtcNow();
        var wait = asset.RetryAfter is { } at ? at - now : (TimeSpan?)null;

        return asset.Status switch
        {
            GeneratedAssetStatus.Ready => Results.Json(new { status = "ready", url = asset.Url }),
            GeneratedAssetStatus.Pending => RetryAfter(Results.Json(new { status = "pending", retryAfter = asset.RetryAfter }, statusCode: StatusCodes.Status202Accepted), wait),
            GeneratedAssetStatus.Failed => RetryAfter(Results.Json(
                new { status = "failed", reason = JsonName(asset.Failure), retryAfter = asset.RetryAfter }, statusCode: StatusCodes.Status502BadGateway), wait),
            GeneratedAssetStatus.Off => new AIFeatureOffException(feature, asset.Off ?? AIOffReason.NotConfigured).ToResult(),
            GeneratedAssetStatus.RateLimited => new AIRateLimitedException(feature, wait).ToResult(),
            _ => Results.Json(new { status = "notFound" }, statusCode: StatusCodes.Status404NotFound),
        };
    }

    private static string? JsonName(GeneratedAssetFailure? failure) =>
        failure is { } known ? JsonNamingPolicy.CamelCase.ConvertName(known.ToString()) : null;

    private static IResult RetryAfter(IResult inner, TimeSpan? wait) => new WithHeader(inner, "Retry-After",
        wait is { } w ? Math.Max(1, (int)Math.Ceiling(w.TotalSeconds)).ToString(CultureInfo.InvariantCulture) : null);

    private sealed class WithHeader(IResult inner, string name, string? value) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            if (value is not null) httpContext.Response.Headers[name] = value;
            return inner.ExecuteAsync(httpContext);
        }
    }
}

/// <summary>Maps the assets endpoint onto an ASP.NET Core application.</summary>
public static class SpineAIAssetEndpointRouteBuilderExtensions
{
    /// <summary>Maps <c>GET {prefix}/assets/{feature}/{key}</c>, where the key may hold <c>/</c>.</summary>
    /// <param name="endpoints">The application's route builder.</param>
    /// <param name="prefix">The path the endpoint lives under; the same as <c>MapSpineAI</c>'s.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSpineAIAssets(this IEndpointRouteBuilder endpoints, string prefix = "/ai")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        endpoints.MapGet($"{prefix.TrimEnd('/')}/assets/{{feature}}/{{**key}}",
            (HttpRequest request, string feature, string key, CancellationToken ct) => SpineAIAssetEndpoints.GetAsync(request, feature, key, ct));
        return endpoints;
    }
}
