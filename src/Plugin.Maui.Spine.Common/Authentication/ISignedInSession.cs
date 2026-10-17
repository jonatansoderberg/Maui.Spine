namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// The signed-in user as other Spine packages see it, without a reference to <c>Plugin.Maui.Spine.Authentication</c>.
/// Registered by that package when <c>LinkPushNotifications</c> is on; PushNotifications then registers the device
/// with the user's access token and again whenever the user changes.
/// </summary>
public interface ISignedInSession
{
    /// <summary>The signed-in user's id, or <see langword="null"/>.</summary>
    string? UserId { get; }

    /// <summary>The <c>Authorization</c> header for the app's server (<c>Bearer …</c>), refreshed as needed; <see langword="null"/> when signed out.</summary>
    Task<string?> GetAuthorizationHeaderAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised after a sign-in and after a sign-out.</summary>
    event EventHandler? Changed;
}
