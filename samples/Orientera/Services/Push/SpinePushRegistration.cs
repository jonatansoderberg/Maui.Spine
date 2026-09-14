using Orientera.Services.Notifications;
using Plugin.Maui.Spine.PushNotifications;

namespace Orientera.Services.Push;

/// <summary>
/// The notification service's view of Spine.PushNotifications. Kept out of <c>Services/Notifications</c> on
/// purpose: that folder is compiled into the tests on plain .NET, where the MAUI package does not
/// exist.
/// </summary>
public sealed class SpinePushRegistration(IPushNotificationService _push) : IPushRegistration
{
    public bool IsRegistered => _push.IsRegistered;

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
        await _push.RequestPermissionAsync(cancellationToken) is PushStatus.Authorized or PushStatus.Provisional;

    public Task SetTagsAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default) =>
        _push.SetTagsAsync(tags, cancellationToken);
}
