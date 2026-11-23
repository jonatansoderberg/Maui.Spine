using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Windows: widgets on the Windows 11 widget board, for an app packaged as MSIX whose manifest the build gave the
/// provider. The timeline documents and pictures live under the app's local data; the board gets an Adaptive Card per
/// widget, drawn here from the entry that applies now, and is sent a new one when the app rebuilds, at the next
/// entry's date while the process runs, and whenever the board shows the widget again. There are no Live Activities
/// and no controls on Windows.
/// </summary>
internal sealed class NativeWidgetPlatform : IWidgetPlatform
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(15);

    private readonly WidgetBoardManifest? _manifest;
    private readonly WindowsWidgetPalette _palette = new();
    private readonly ConcurrentDictionary<string, Timer> _timers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.Ordinal);

    public NativeWidgetPlatform(ILogger<NativeWidgetPlatform> logger)
    {
        Logger = logger;
        try { _manifest = WidgetBoardManifest.Read(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            logger.LogWarning(e, "The package manifest could not be read; widgets are disabled.");
        }

        if (AppInfo.Current.PackagingModel != AppPackagingModel.Packaged)
            logger.LogInformation("The app is not packaged; the Windows widget board shows widgets of MSIX-packaged apps only.");
        else if (_manifest is null)
            logger.LogWarning("The package manifest declares no widget provider; widgets are disabled. Is build/Plugin.Maui.Spine.Widgets.targets imported and at least one <SpineWidget> declared?");
    }

    public ILogger Logger { get; }

    public Guid? ClassId => _manifest?.ClassId;

    public bool IsSupported => _manifest is { Kinds.Count: > 0 };

    private static string Root => Path.Combine(FileSystem.AppDataDirectory, "spine-widgets");
    private static string TimelinePath(string kind) => Path.Combine(Root, kind + ".json");
    private static string RemoteCachePath(string kind) => Path.Combine(Root, kind + ".remote.json");
    private static string AssetsDirectory => Path.Combine(Root, "assets");

    public void WriteTimeline(string kind, string json)
    {
        if (IsSupported) AtomicFile.WriteAllText(TimelinePath(kind), json);
    }

    public Task StoreAssetAsync(string assetId, Stream png, CancellationToken cancellationToken) =>
        IsSupported ? AtomicFile.WriteAsync(Path.Combine(AssetsDirectory, assetId), png, cancellationToken) : Task.CompletedTask;

    public string? AssetPath(string assetId) =>
        Path.Combine(AssetsDirectory, assetId) is var path && File.Exists(path) ? path : null;

    private static byte[]? ReadAsset(string assetId)
    {
        var path = Path.Combine(AssetsDirectory, assetId);
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (IOException) { return null; }
    }

    /// <summary>The board cannot fetch, so the app does, at each rebuild and at the refresh time while it runs.</summary>
    public async Task FetchRemoteAsync(string kind, Uri source, CancellationToken cancellationToken)
    {
        if (!IsSupported) return;
        var json = await Http.GetStringAsync(source, cancellationToken);
        using (var check = JsonDocument.Parse(json))
            if (!check.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("not a timeline document");
        AtomicFile.WriteAllText(RemoteCachePath(kind), json);
    }

    public void Reload(string kind)
    {
        if (IsSupported) Show(kind);
    }

    public void ReloadAll()
    {
        foreach (var kind in _manifest?.Kinds ?? []) Show(kind);
    }

    /// <summary>The widgets of <paramref name="kind"/> on the board, by id and size; none when the board cannot be reached.</summary>
    private List<(string Id, WidgetSize Size)> Placed(string kind)
    {
        try
        {
            return [.. WidgetManager.GetDefault().GetWidgetInfos()
                .Select(info => info.WidgetContext)
                .Where(context => context.DefinitionId == kind)
                .Select(context => (context.Id, context.Size))];
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            Logger.LogWarning(e, "The widget board could not be asked for the widgets of \"{Kind}\".", kind);
            return [];
        }
    }

    /// <summary>
    /// Sends the entry that applies now to the widgets of <paramref name="kind"/>, or to the one widget
    /// <paramref name="widgetId"/>, and books the next change. A widget whose kind has no document yet shows a dash
    /// and, with <paramref name="refreshWhenEmpty"/>, has the provider run.
    /// </summary>
    public void Show(string kind, string? widgetId = null, WidgetSize? size = null, bool refreshWhenEmpty = false)
    {
        if (!IsSupported) return;
        // The board calls in on the UI thread; drawing pictures and encoding them is not for that thread.
        Task.Run(() =>
        {
            lock (_locks.GetOrAdd(kind, _ => new object()))
                Draw(kind, widgetId is null ? Placed(kind) : [(widgetId, size ?? WidgetSize.Small)], refreshWhenEmpty);
        }).SafeFireAndForget(e => Logger.LogError(e, "Drawing widget \"{Kind}\" for the widget board failed.", kind));
    }

    private void Draw(string kind, List<(string Id, WidgetSize Size)> widgets, bool refreshWhenEmpty)
    {
        if (widgets.Count == 0) return;
        var local = Read(TimelinePath(kind));
        if (local is null)
        {
            foreach (var (id, _) in widgets) Update(id, Placeholder);
            if (refreshWhenEmpty && IPlatformApplication.Current?.Services.GetService<IWidgetService>() is { } service)
                service.RefreshAsync(kind).SafeFireAndForget(e => Logger.LogError(e, "Building widget \"{Kind}\" for the widget board failed.", kind));
            return;
        }

        var hasRemote = local["remote"] is JsonValue;
        var root = (hasRemote ? Read(RemoteCachePath(kind)) : null) ?? local;
        var entries = (root["entries"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(e => (Date: DateTimeOffset.TryParse(e["date"]?.GetValue<string>(), out var d) ? d : DateTimeOffset.MinValue, Entry: e))
            .OrderBy(e => e.Date)
            .ToList();
        if (entries.Count == 0) return;

        var now = DateTimeOffset.Now;
        var current = entries.LastOrDefault(e => e.Date <= now) is { Entry: not null } shown ? shown.Entry : entries[0].Entry;
        var link = (root["link"] ?? local["link"])?.GetValue<string>();
        // The surface travels whole, as on Android: the entry's, else the remote document's, else the app's.
        var surface = Surface(HasSurface(current) ? current : HasSurface(root) ? root : local);
        var trees = current["trees"] as JsonObject;

        var clock = false;
        foreach (var (id, size) in widgets)
        {
            var area = Area(size);
            // The family of the size's shape the timeline has a tree for; with one tree for all, the first, for W.Adaptive.
            var family = area.Families.FirstOrDefault(f => trees?.ContainsKey(f) == true) ?? area.Families[0];
            var tree = trees?[family] ?? trees?[WidgetJson.DefaultFamilyKey] ?? trees?.Select(t => t.Value).FirstOrDefault();
            var place = new DrawPlace("windows", ["homeScreen"], family, Environment.OSVersion.Version.Build >= 22000 ? 11 : 10);
            var renderer = new AdaptiveCardRenderer(ReadAsset, _palette, family, area, now);
            var card = renderer.Card(WidgetOverrides.Apply(tree, place) ?? new JsonObject { ["type"] = "vstack" }, surface, link);
            Update(id, card.ToJsonString());
            clock |= renderer.HasClock && WidgetBoardProvider.Instance.IsActive(id);
        }

        var refreshAfter = root["refreshAfterSeconds"] is JsonValue refresh && refresh.TryGetValue<double>(out var seconds) ? seconds
            : hasRemote ? MinimumRefreshInterval.TotalSeconds : (double?)null;
        Schedule(kind, entries.Select(e => e.Date).ToList(), refreshAfter, clock, now);
    }

    /// <summary>
    /// The board's sizes are one column wide and differ in height: small a strip about 100 points high, medium near
    /// square, large tall. Each draws the family of its name, which holds about as much; a timeline without that
    /// family's tree draws the nearest. Measured on Windows 11 (build 26300) at 150 %.
    /// </summary>
    internal static BoardArea Area(WidgetSize size) => size switch
    {
        WidgetSize.Small => new(268, 144, ["small", "medium", "large"]),
        WidgetSize.Medium => new(268, 304, ["medium", "small", "large"]),
        _ => new(268, 458, ["large", "medium", "small"]),
    };

    private void Update(string widgetId, string template)
    {
        try
        {
            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(widgetId) { Template = template, Data = "{}" });
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException)
        {
            Logger.LogWarning(e, "The widget board refused the card of widget {Id} ({Length} characters).", widgetId, template.Length);
        }
    }

    /// <summary>
    /// The card is a snapshot, and nothing turns it without this process: a timer at the next entry's date, every
    /// minute while the board shows a count, and at <c>refreshAfterSeconds</c> after the last entry to run the provider.
    /// When the process has ended, the board's next <see cref="WidgetBoardProvider.Activate"/> draws the entry of the moment.
    /// </summary>
    private void Schedule(string kind, List<DateTimeOffset> dates, double? refreshAfterSeconds, bool clock, DateTimeOffset now)
    {
        DateTimeOffset? redraw = dates.FirstOrDefault(d => d > now) is var next && next != default ? next : null;
        if (clock)
        {
            var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset).AddMinutes(1);
            if (redraw is null || minute < redraw) redraw = minute;
        }
        DateTimeOffset? rebuild = refreshAfterSeconds is { } seconds
            ? dates[^1].AddSeconds(seconds) is var at && at > now ? at : now + MinimumRefreshInterval
            : null;

        var (when, run) = (redraw, rebuild) switch
        {
            ({ } r, { } b) when b <= r => (b, true),
            ({ } r, _) => (r, false),
            (null, { } b) => (b, true),
            _ => ((DateTimeOffset?)null, false),
        };

        if (_timers.TryRemove(kind, out var old)) old.Dispose();
        if (when is not { } due) return;
        var delay = due - DateTimeOffset.Now;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        if (delay > TimeSpan.FromDays(7)) delay = TimeSpan.FromDays(7);
        _timers[kind] = new Timer(_ =>
        {
            if (run && IPlatformApplication.Current?.Services.GetService<IWidgetService>() is { } service)
                service.RefreshAsync(kind).SafeFireAndForget(e => Logger.LogError(e, "Refreshing widget \"{Kind}\" on its schedule failed.", kind));
            else Show(kind);
        }, null, delay + TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
    }

    private static readonly string Placeholder = new JsonObject
    {
        ["type"] = "AdaptiveCard",
        ["version"] = "1.5",
        ["verticalContentAlignment"] = "center",
        ["body"] = new JsonArray(new JsonObject { ["type"] = "TextBlock", ["text"] = "—", ["isSubtle"] = true, ["horizontalAlignment"] = "center" }),
    }.ToJsonString();

    private JsonObject? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Logger.LogWarning(e, "{Path} failed to load.", path);
            return null;
        }
    }

    private static bool HasSurface(JsonObject document) =>
        document.ContainsKey("background") || document.ContainsKey("backgroundGradient") || document.ContainsKey("backgroundImage");

    private static WidgetSurfaceFields Surface(JsonObject document) => new(
        Text(document["background"]),
        document["backgroundGradient"]?["colors"] is JsonArray colors ? [.. colors.Select(Text).OfType<string>()] : null,
        Text(document["backgroundGradient"]?["direction"]),
        Text(document["backgroundImage"]));

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    // Windows has neither Live Activities nor controls.
    public bool AreActivitiesSupported => false;
    public bool AreActivitiesEnabled => false;
    public IReadOnlyDictionary<string, string> ActiveActivities() => ReadOnlyDictionary<string, string>.Empty;
    public Task<string?> StartActivityAsync(string kind, string json, DateTimeOffset? staleAt, string? channel) => Task.FromResult<string?>(null);
    public void UpdateActivity(string id, string json, DateTimeOffset? staleAt) { }
    public void EndActivity(string id) { }
    public string? PushToStartToken => null;
    public string? PushToken(string id) => null;
    public string? WidgetPushToken => null;
    public void RefreshWidgetPushToken() { }
    public bool AreControlsSupported => false;
    public void WriteControl(string kind, string json) { }
    public void ReloadControl(string kind) { }
    public Task<bool> RequestAddControlAsync(string kind, ControlState state) => Task.FromResult(false);
}
