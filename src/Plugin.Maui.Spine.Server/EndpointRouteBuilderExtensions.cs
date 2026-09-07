using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Plugin.Maui.Spine.Server;

/// <summary>Maps the registration endpoints onto an ASP.NET Core application.</summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>PUT</c> and <c>DELETE</c> on <c>{prefix}/installations/{id}</c>. Authentication is the
    /// backend's business; set <see cref="SpinePushOptions.Authenticate"/> to gate them.
    /// </summary>
    /// <param name="endpoints">The application's route builder.</param>
    /// <param name="prefix">The path the endpoints live under.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSpinePush(this IEndpointRouteBuilder endpoints, string prefix = "/push")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var route = $"{prefix.TrimEnd('/')}/installations/{{id}}";

        endpoints.MapPut(route, (HttpRequest request, CancellationToken ct) => SpinePushEndpoints.HandleAsync(request, ct));
        endpoints.MapDelete(route, (HttpRequest request, CancellationToken ct) => SpinePushEndpoints.HandleAsync(request, ct));

        return endpoints;
    }
}
