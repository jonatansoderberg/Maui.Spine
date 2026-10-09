using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.PushNotifications;

/// <summary>The logger the Windows half writes to, made once the app's services exist.</summary>
internal static class WindowsLog
{
    private static ILogger? _logger;

    internal static ILogger? Logger =>
        _logger ??= IPlatformApplication.Current?.Services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.PushNotifications");
}
