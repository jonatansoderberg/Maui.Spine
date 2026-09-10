using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>
/// What each platform has to answer for the shared registration logic to work. One implementation
/// per platform, registered by <c>ConfigurePlatform</c>.
/// </summary>
internal interface IPushPlatform
{
    /// <summary>Which service this device is reached through.</summary>
    PushPlatform Platform { get; }

    /// <summary>Whether the app may notify, as of the last time the system was asked.</summary>
    PushStatus Status { get; }

    /// <summary>The device token, FCM registration token, or channel URI; <see langword="null"/> until one arrives.</summary>
    string? Handle { get; }

    /// <summary>Which APNs host <see cref="Handle"/> belongs to. <see langword="null"/> off Apple platforms.</summary>
    ApnsEnvironment? Environment { get; }

    /// <summary>Asks the user, unless they have already been asked.</summary>
    /// <param name="permission">Which kind of authorization to ask for.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The status afterwards.</returns>
    Task<PushStatus> RequestPermissionAsync(PushPermission permission, CancellationToken cancellationToken);

    /// <summary>Opens the system settings page for this app's notifications.</summary>
    Task OpenSettingsAsync();

    /// <summary>Raised when the platform hands over a new token.</summary>
    event Action<string>? HandleChanged;
}
