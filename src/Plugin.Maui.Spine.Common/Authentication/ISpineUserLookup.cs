using System.Security.Claims;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// Finds a signed-in user's details for other Spine server packages, without a reference to the package that keeps the
/// users. Spine.Server.Authentication registers it; AI functions read the user through it
/// (<c>SpineAIFunctionContext.GetUserAsync</c>).
/// </summary>
public interface ISpineUserLookup
{
    /// <summary>The user with id <paramref name="userId"/>, or <see langword="null"/> when there is none.</summary>
    /// <param name="userId">The user's id, the access token's <c>sub</c>.</param>
    /// <param name="principal">The request's user, for the provider they signed in with.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<SpineUser?> FindAsync(string userId, ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
