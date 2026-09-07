using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// The registration endpoints, in a form both hosts can use: ASP.NET routing calls them through
/// <see cref="EndpointRouteBuilderExtensions.MapSpinePush"/>, and an Azure Functions isolated worker
/// calls <see cref="HandleAsync"/> from its own <c>[Function]</c>.
/// </summary>
/// <remarks>
/// The contract is the same either way: <c>PUT /push/installations/{id}</c> registers or updates,
/// <c>DELETE /push/installations/{id}</c> removes.
/// </remarks>
public static class SpinePushEndpoints
{
    private const string Segment = "/installations/";

    /// <summary>Handles one registration request, whatever host it arrived through.</summary>
    /// <param name="request">The incoming request; its services provide the register and the options.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The result to return from the host's own handler.</returns>
    public static async Task<IResult> HandleAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var services = request.HttpContext.RequestServices;
        var options = services.GetRequiredService<SpinePushOptions>();
        var store = services.GetRequiredService<IPushInstallationStore>();

        if (options.Authenticate is { } authenticate && !await authenticate(request))
            return Results.Unauthorized();

        if (IdFrom(request.Path) is not { } id)
            return Results.BadRequest($"The path must end in {Segment}{{id}}.");

        return request.Method.ToUpperInvariant() switch
        {
            "PUT" => await UpsertAsync(request, store, options, id, cancellationToken),
            "DELETE" => await DeleteAsync(store, id, cancellationToken),
            _ => Results.StatusCode(StatusCodes.Status405MethodNotAllowed),
        };
    }

    internal static async Task<IResult> UpsertAsync(
        HttpRequest request, IPushInstallationStore store, SpinePushOptions options, string id, CancellationToken cancellationToken)
    {
        PushInstallation? body;

        try
        {
            using var reader = new StreamReader(request.Body);
            body = PushJson.Deserialize(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (System.Text.Json.JsonException e)
        {
            return Results.BadRequest($"The body is not an installation: {e.Message}");
        }

        if (body is null) return Results.BadRequest("The body is empty.");

        if (!string.Equals(body.Id, id, StringComparison.Ordinal))
            return Results.BadRequest($"The body's id '{body.Id}' does not match the path's '{id}'.");

        if (string.IsNullOrWhiteSpace(body.Handle))
            return Results.BadRequest("The installation has no handle.");

        // The clock is the server's: a device with a wrong date must not look freshly registered,
        // since that is what Prune goes by.
        var installation = body with
        {
            Tags = options.FilterTags(body, body.Tags),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await store.UpsertAsync(installation, cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> DeleteAsync(IPushInstallationStore store, string id, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(id, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>The installation id in <paramref name="path"/>, or <see langword="null"/> when there is none.</summary>
    internal static string? IdFrom(string path)
    {
        var at = path.LastIndexOf(Segment, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        var id = path[(at + Segment.Length)..].Trim('/');
        return string.IsNullOrWhiteSpace(id) ? null : Uri.UnescapeDataString(id);
    }
}
