using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Windows.UI.Notifications;

namespace Plugin.Maui.Spine.PushNotifications.Services;

/// <summary>
/// Local notifications on Windows: scheduled toasts, built like the toasts a push brings, so a tapped one
/// takes the same way back to the handler.
/// </summary>
/// <remarks>
/// Scheduling needs package identity: <c>ToastNotificationManager.CreateToastNotifier()</c> answers for
/// the package, and the Windows App SDK's <c>AppNotificationManager</c> can show a toast but not schedule
/// one. An unpackaged app (<c>WindowsPackageType=None</c>) is therefore <see cref="IsSupported"/> false,
/// and the first call that would have scheduled something says so in the log.
/// </remarks>
internal sealed class WindowsLocalNotifications(SpinePushNotificationsOptions options) : ILocalNotificationService
{
    private const string Group = "spine.local";

    private bool _warned;

    public bool IsSupported => AppInfo.Current.PackagingModel == AppPackagingModel.Packaged;

    public Task SyncAsync(IEnumerable<LocalNotification> plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (Notifier() is not { } notifier) return Task.CompletedTask;

        foreach (var scheduled in notifier.GetScheduledToastNotifications())
        {
            if (scheduled.Group == Group) notifier.RemoveFromSchedule(scheduled);
        }

        var now = DateTimeOffset.Now;
        foreach (var notification in plan.Where(n => n.At > now).OrderBy(n => n.At))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var xml = new Windows.Data.Xml.Dom.XmlDocument();
                xml.LoadXml(WindowsNotifications.Toast(LocalNotificationPayload.Write(notification), options));

                notifier.AddToSchedule(new ScheduledToastNotification(xml, notification.At)
                {
                    Tag = WindowsNotifications.Tag(notification.Id),
                    Group = Group,
                });
            }
            catch (Exception e)
            {
                Logger?.LogWarning(e, "Spine.PushNotifications: scheduling {Id} failed (HRESULT 0x{HResult:X8}).", notification.Id, e.HResult);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LocalNotification>> PendingAsync(CancellationToken cancellationToken = default)
    {
        if (Notifier() is not { } notifier) return Task.FromResult<IReadOnlyList<LocalNotification>>([]);

        var now = DateTimeOffset.Now;
        IReadOnlyList<LocalNotification> pending =
        [
            .. notifier.GetScheduledToastNotifications()
                .Where(s => s.Group == Group)
                .Select(Read)
                .OfType<LocalNotification>()
                .Where(n => n.At > now)
                .OrderBy(n => n.At),
        ];

        return Task.FromResult(pending);
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default) => SyncAsync([], cancellationToken);

    private ToastNotifier? Notifier()
    {
        if (IsSupported) return ToastNotificationManager.CreateToastNotifier();

        if (!_warned)
        {
            _warned = true;
            Logger?.LogWarning("Spine.PushNotifications: local notifications on Windows need package identity (MSIX), and this app runs unpackaged; nothing is scheduled.");
        }

        return null;
    }

    /// <summary>The planned notification back from the toast's launch argument, which carries the whole bag.</summary>
    private static LocalNotification? Read(ScheduledToastNotification scheduled)
    {
        var data = WnsPayload.ReadArguments(scheduled.Content.DocumentElement.GetAttribute("launch"));
        if (!data.TryGetValue(PushKeys.Collapse, out var id) || !data.TryGetValue(PushKeys.Title, out var title)) return null;

        return new LocalNotification
        {
            Id = id,
            At = long.TryParse(data.GetValueOrDefault(LocalNotificationPayload.At), out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime()
                : scheduled.DeliveryTime,
            Title = title,
            Body = data.GetValueOrDefault(PushKeys.Body),
            Route = data.GetValueOrDefault(PushKeys.Route),
            Channel = data.GetValueOrDefault(PushKeys.Channel),
            Category = data.GetValueOrDefault(PushKeys.Category),
            Image = data.GetValueOrDefault(PushKeys.Image),
            Sound = data.GetValueOrDefault(LocalNotificationPayload.Sound),
            Data = LocalNotificationPayload.App(data),
        };
    }

    private static ILogger? Logger =>
        IPlatformApplication.Current?.Services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.PushNotifications");
}
