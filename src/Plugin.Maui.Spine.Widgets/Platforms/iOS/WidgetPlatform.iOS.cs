using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Foundation;
using Microsoft.Extensions.Logging;
using ObjCRuntime;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// iOS: the timeline documents live in the App Group container the extension reads, and WidgetKit
/// and ActivityKit are reached through the Swift bridge framework the build compiles alongside the
/// extension. The bridge is an <c>@objc</c> class, so plain <c>objc_msgSend</c> is enough — no binding project.
/// </summary>
internal sealed class WidgetPlatform : IWidgetPlatform
{
    private const string BridgeClassName = "SpineWidgetBridge";
    private const string AppGroupInfoKey = "SpineWidgetsAppGroup";

    private readonly ILogger<WidgetPlatform> _logger;
    private readonly IntPtr _bridge;
    private readonly string? _appGroup;
    private readonly string? _containerPath;

    public WidgetPlatform(SpineWidgetsOptions options, ILogger<WidgetPlatform> logger)
    {
        _logger = logger;
        _bridge = Class.GetHandle(BridgeClassName);
        _appGroup = options.AppGroup ?? NSBundle.MainBundle.ObjectForInfoDictionary(AppGroupInfoKey)?.ToString();

        if (_bridge == IntPtr.Zero)
            logger.LogWarning("The {Bridge} framework is not in the app bundle; widgets are disabled. Is build/Plugin.Maui.Spine.Widgets.targets imported and at least one <SpineWidget> declared?", BridgeClassName);
        else if (_appGroup is null)
            logger.LogWarning("No App Group configured; widgets are disabled. Set SpineWidgetsAppGroup in the project or SpineWidgetsOptions.AppGroup.");
        else if (NSFileManager.DefaultManager.GetContainerUrl(_appGroup)?.Path is { } path)
            _containerPath = Path.Combine(path, "spine-widgets");
        else
            logger.LogWarning("The App Group container \"{Group}\" is not available; check the app's entitlements.", _appGroup);
    }

    public bool IsSupported => _bridge != IntPtr.Zero && _containerPath is not null;

    public void WriteTimeline(string kind, string json)
    {
        if (_containerPath is null) return;
        Directory.CreateDirectory(_containerPath);
        var target = Path.Combine(_containerPath, kind + ".json");
        var temp = target + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, target, overwrite: true);
    }

    public async Task StoreAssetAsync(string assetId, Stream png, CancellationToken cancellationToken)
    {
        if (_containerPath is null) return;
        var assets = Path.Combine(_containerPath, "assets");
        Directory.CreateDirectory(assets);
        await using var file = File.Create(Path.Combine(assets, assetId));
        await png.CopyToAsync(file, cancellationToken);
    }

    public void Reload(string kind)
    {
        if (!IsSupported) return;
        using var value = new NSString(kind);
        Send(_bridge, Selector.GetHandle("reloadWithKind:"), value.Handle);
    }

    public void ReloadAll()
    {
        if (!IsSupported) return;
        Send(_bridge, Selector.GetHandle("reloadAll"));
    }

    public bool AreActivitiesEnabled => IsSupported && SendBool(_bridge, Selector.GetHandle("activitiesEnabled")) != 0;

    public IReadOnlyDictionary<string, string> ActiveActivities()
    {
        if (!IsSupported) return ReadOnlyDictionary<string, string>.Empty;

        var handle = SendObject(_bridge, Selector.GetHandle("activeActivities"));
        if (handle == IntPtr.Zero) return ReadOnlyDictionary<string, string>.Empty;

        using var dictionary = Runtime.GetNSObject<NSDictionary>(handle, owns: false);
        return dictionary is null
            ? ReadOnlyDictionary<string, string>.Empty
            : dictionary.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToString());
    }

    public string? StartActivity(string kind, string json, DateTimeOffset? staleAt)
    {
        if (!IsSupported) return null;
        using var kindValue = new NSString(kind);
        using var jsonValue = new NSString(json);
        var id = SendObject(_bridge, Selector.GetHandle("startActivityWithKind:json:staleAt:"), kindValue.Handle, jsonValue.Handle, Seconds(staleAt));
        var result = id == IntPtr.Zero ? null : NSString.FromHandle(id);
        if (result is null) _logger.LogWarning("Starting Live Activity \"{Kind}\" was refused by the system.", kind);
        return result;
    }

    public void UpdateActivity(string id, string json, DateTimeOffset? staleAt)
    {
        if (!IsSupported) return;
        using var idValue = new NSString(id);
        using var jsonValue = new NSString(json);
        Send(_bridge, Selector.GetHandle("updateActivityWithId:json:staleAt:"), idValue.Handle, jsonValue.Handle, Seconds(staleAt));
    }

    public void EndActivity(string id)
    {
        if (!IsSupported) return;
        using var idValue = new NSString(id);
        Send(_bridge, Selector.GetHandle("endActivityWithId:"), idValue.Handle);
    }

    // 0 means "no stale date" on the Swift side.
    private static double Seconds(DateTimeOffset? at) => at?.ToUnixTimeSeconds() ?? 0;

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void Send(IntPtr receiver, IntPtr selector);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void Send(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, double arg3);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendObject(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, double arg3);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendObject(IntPtr receiver, IntPtr selector);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern byte SendBool(IntPtr receiver, IntPtr selector);
}
