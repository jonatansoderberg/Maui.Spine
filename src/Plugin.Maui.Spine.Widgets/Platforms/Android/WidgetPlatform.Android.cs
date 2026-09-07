using Android.Content;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Android: the timeline documents live in app-internal storage the receivers read, the widgets are
/// redrawn through <c>AppWidgetManager</c>, and Live Activities are promoted notifications.
/// Everything is C#; the build only contributes manifest entries and the provider metadata.
/// </summary>
internal sealed class WidgetPlatform : IWidgetPlatform
{
    private readonly Context _context = Android.App.Application.Context;
    private readonly string[] _kinds;
    private readonly ILogger<WidgetPlatform> _logger;
    private readonly LiveUpdateNotifications? _live;

    public WidgetPlatform(ILogger<WidgetPlatform> logger)
    {
        _logger = logger;
        _kinds = WidgetStore.Kinds(_context);

        // Live Updates — promoted ongoing notifications — arrived with Android 16; older versions have no
        // Live Activity, and a plain notification would not be one.
        if (OperatingSystem.IsAndroidVersionAtLeast(36))
            _live = new LiveUpdateNotifications(_context, new WidgetIcons(_context), logger);

        if (_kinds.Length == 0)
            logger.LogWarning("No widget receivers in the manifest; widgets are disabled. Is build/Plugin.Maui.Spine.Widgets.targets imported and at least one <SpineWidget> declared?");
    }

    public bool IsSupported => _kinds.Length > 0;

    public void WriteTimeline(string kind, string json)
    {
        if (!IsSupported) return;
        var target = WidgetStore.TimelinePath(_context, kind);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, target, overwrite: true);
    }

    public async Task StoreAssetAsync(string assetId, Stream png, CancellationToken cancellationToken)
    {
        if (!IsSupported) return;
        var target = Path.Combine(WidgetStore.AssetsDirectory(_context), assetId);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var file = File.Create(target);
        await png.CopyToAsync(file, cancellationToken);
    }

    public void Reload(string kind)
    {
        if (IsSupported) SpineAppWidget.Update(_context, kind);
    }

    public void ReloadAll()
    {
        foreach (var kind in _kinds) SpineAppWidget.Update(_context, kind);
    }

    // The notification permission cannot be told apart from "never asked" here, so it is not a reason to say no;
    // StartActivityAsync asks for it. What remains is the Android version.
    public bool AreActivitiesEnabled => IsSupported && OperatingSystem.IsAndroidVersionAtLeast(36);

    public IReadOnlyDictionary<string, string> ActiveActivities() =>
        OperatingSystem.IsAndroidVersionAtLeast(36) ? _live!.Active() : new Dictionary<string, string>();

    public async Task<string?> StartActivityAsync(string kind, string json, DateTimeOffset? staleAt)
    {
        if (!IsSupported) return null;
        if (!OperatingSystem.IsAndroidVersionAtLeast(36))
        {
            _logger.LogWarning("Live Activity \"{Kind}\" not started: Live Updates need Android 16.", kind);
            return null;
        }

        // Unlike iOS there is a runtime permission in the way, and a start is the moment the user expects a
        // prompt: it is what the button they just pressed does. Denied once, the system returns denied silently.
        if (await Permissions.RequestAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
        {
            _logger.LogWarning("Live Activity \"{Kind}\" not started: the notification permission was not granted.", kind);
            return null;
        }

        return _live!.Start(kind, json);
    }

    public void UpdateActivity(string id, string json, DateTimeOffset? staleAt)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(36)) _live!.Update(id, json);
    }

    public void EndActivity(string id)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(36)) _live!.End(id);
    }

    // Android Live Updates are driven by the app; a server reaches them through the app's own push handler.
    public string? PushToStartToken => null;

    public string? PushToken(string id) => null;
}
