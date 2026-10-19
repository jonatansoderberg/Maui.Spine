using System.Security.Claims;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>
/// The users as other Spine packages read them: an AI function's <c>GetUserAsync</c> gets the app's view of the user
/// (<see cref="SpineUser"/>), never the password hash or the lock state.
/// </summary>
internal sealed class SpineUserLookup(IUserResolver users) : ISpineUserLookup
{
    public async Task<SpineUser?> FindAsync(string userId, ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        if (await users.FindByIdAsync(userId, cancellationToken) is not { } user) return null;

        var provider = principal.FindFirst(SpineAuthDefaults.ProviderClaim)?.Value ?? SpineAuthDefaults.PasswordProvider;
        return user.ToContract(provider, await users.GetLoginsAsync(user.Id, cancellationToken));
    }
}
