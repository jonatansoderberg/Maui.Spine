using Foundation;
using Microsoft.Extensions.Logging;
using ObjCRuntime;
using Plugin.Maui.Spine.Common;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>A button tap as the intent recorded it in <c>actions.jsonl</c>.</summary>
/// <param name="Id">The intent's own id for the tap, which <see cref="WidgetPlatform.CompleteAction"/> answers; <see langword="null"/> for a line without one.</param>
/// <param name="Kind">The widget kind the button belongs to.</param>
/// <param name="ActionId">The id given to <see cref="W.Button"/>.</param>
/// <param name="At">When the button was tapped.</param>
internal readonly record struct RecordedAction(string? Id, string Kind, string ActionId, DateTimeOffset At);

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
    private readonly object _actionsLock = new();
    private readonly IntPtr _bridge;
    private readonly string? _appGroup;
    private readonly string? _containerPath;

    public WidgetPlatform(SpineWidgetsOptions options, ILogger<WidgetPlatform> logger)
    {
        _logger = logger;
        _bridge = Class.GetHandle(BridgeClassName);
        _appGroup = options.AppGroup ?? NSBundle.MainBundle.ObjectForInfoDictionary(AppGroupInfoKey)?.ToString();

        if (_bridge != IntPtr.Zero && options.LiveActivityPushTokens)
            Send(_bridge, Selector.GetHandle("enablePushTokens"));

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

    /// <summary>The Darwin notification the button intent posts after recording a tap.</summary>
    public string? ActionNotificationName => _appGroup is null ? null : _appGroup + ".spine-widgets.action";

    /// <summary>The Darwin notification the bridge posts when an activity was dismissed by the user or ended.</summary>
    public string? ActivityNotificationName => _appGroup is null ? null : _appGroup + ".spine-widgets.activity";

    /// <summary>The Darwin notification the extension's push handler and the bridge post when the widget push token changed.</summary>
    public string? PushTokenNotificationName => _appGroup is null ? null : _appGroup + ".spine-widgets.push-token";

    /// <summary>Starts the bridge's watch on every activity's state; what makes <see cref="ActivityNotificationName"/> fire.</summary>
    public void ObserveActivities()
    {
        if (!IsSupported) return;
        Send(_bridge, Selector.GetHandle("observeActivities"));
    }

    /// <summary>
    /// Reads and clears the button taps the intent recorded, oldest first. Serialized: the Darwin
    /// notification and the drain at launch can arrive together, and a tap must be handled once.
    /// </summary>
    public IReadOnlyList<RecordedAction> TakeActions()
    {
        if (_containerPath is null) return [];
        var path = Path.Combine(_containerPath, "actions.jsonl");

        string[] lines;
        lock (_actionsLock)
        {
            if (!File.Exists(path)) return [];
            try { lines = File.ReadAllLines(path); File.Delete(path); }
            catch (IOException) { return []; }
        }

        var actions = new List<RecordedAction>();
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("kind", out var kind) && root.TryGetProperty("actionId", out var action)
                    && kind.GetString() is { Length: > 0 } k && action.GetString() is { Length: > 0 } a)
                    actions.Add(new RecordedAction(root.TryGetProperty("id", out var id) ? id.GetString() : null, k, a, TappedAt(root)));
            }
            catch (System.Text.Json.JsonException) { }
        }
        return actions;
    }

    /// <summary>
    /// Tells the intent that the tap with this id has been handled and the widget rebuilt. When the
    /// tap ran in the app's process its <c>perform()</c> is waiting for exactly this; iOS may suspend
    /// the background-launched app the moment it returns.
    /// </summary>
    public void CompleteAction(string id)
    {
        if (!IsSupported) return;
        using var value = new NSString(id);
        Send(_bridge, Selector.GetHandle("completeActionWithId:"), value.Handle);
    }

    /// <summary>
    /// The tap's own time, which is the point of recording it: the app can be launched hours after the
    /// tap. Written by the extension as seconds since the epoch. A line without a usable one falls back
    /// to now rather than being dropped — a wrong time is worth less than a lost tap.
    /// </summary>
    private static DateTimeOffset TappedAt(System.Text.Json.JsonElement line) =>
        line.TryGetProperty("at", out var at) && at.ValueKind == System.Text.Json.JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(at.GetDouble() * 1000)).ToLocalTime()
            : DateTimeOffset.Now;

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
        var target = Path.Combine(_containerPath, "assets", assetId);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var file = File.Create(target);
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

    public Task<string?> StartActivityAsync(string kind, string json, DateTimeOffset? staleAt) => Task.FromResult(StartActivity(kind, json, staleAt));

    private string? StartActivity(string kind, string json, DateTimeOffset? staleAt)
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

    public string? PushToStartToken => IsSupported ? Text(SendObject(_bridge, Selector.GetHandle("pushToStartToken"))) : null;

    public string? WidgetPushToken => IsSupported ? Text(SendObject(_bridge, Selector.GetHandle("widgetPushToken"))) : null;

    public void RefreshWidgetPushToken()
    {
        if (IsSupported) Send(_bridge, Selector.GetHandle("refreshWidgetPushToken"));
    }

    public string? PushToken(string id)
    {
        if (!IsSupported) return null;
        using var idValue = new NSString(id);
        return Text(SendObject(_bridge, Selector.GetHandle("pushTokenWithId:"), idValue.Handle));
    }

    private static string? Text(IntPtr handle) => handle == IntPtr.Zero ? null : NSString.FromHandle(handle);

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
    private static extern IntPtr SendObject(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern byte SendBool(IntPtr receiver, IntPtr selector);
}
