using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Runtime;
using Android.Widget;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using System.Net.Http;
using System.Text.Json;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// The receiver behind a widget kind. Android identifies a widget by its receiver class, and the class has
/// to exist at compile time, so the package carries nine and the build's manifest overlay wires the first N
/// to the <c>&lt;SpineWidget&gt;</c> items in declaration order — the same cap as the WidgetKit bundle.
/// Runs in the app's process, so the tree is drawn here from the document the app wrote, and a refresh
/// alarm can run the provider itself.
/// </summary>
internal abstract class SpineAppWidget(int _index) : AppWidgetProvider
{
    private const string Tag = "SpineWidgets";
    private const string ActionRender = "plugin.maui.spine.widgets.RENDER";
    private const string ActionRefresh = "plugin.maui.spine.widgets.REFRESH";
    private const string ActionButton = "plugin.maui.spine.widgets.BUTTON";
    private const string ExtraKind = "plugin.maui.spine.widgets.KIND";
    private const string ExtraAction = "plugin.maui.spine.widgets.ACTION";

    private static readonly Type[] Slots =
    [
        typeof(SpineAppWidget0), typeof(SpineAppWidget1), typeof(SpineAppWidget2),
        typeof(SpineAppWidget3), typeof(SpineAppWidget4), typeof(SpineAppWidget5),
        typeof(SpineAppWidget6), typeof(SpineAppWidget7), typeof(SpineAppWidget8),
    ];

    /// <summary>The sizes the launcher chooses between on API 31+, in dp; keyed by the family names of <c>WidgetJson.FamilyKey</c>.</summary>
    private static readonly (string Family, float Width, float Height)[] Families =
    [
        ("small", 110, 110), ("medium", 250, 110), ("large", 250, 250), ("extraLarge", 340, 250),
    ];

    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly JsonElement Placeholder =
        JsonDocument.Parse("""{"type":"text","text":"—","color":"secondary"}""").RootElement;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        switch (intent.Action)
        {
            case ActionRender when Kind(context) is { } kind:
                Update(context, kind);
                break;
            case ActionRefresh when intent.GetStringExtra(ExtraKind) is { } kind:
                Refresh(context, kind);
                break;
            case ActionButton when intent.GetStringExtra(ExtraKind) is { } kind && intent.GetStringExtra(ExtraAction) is { } actionId:
                Tapped(context, kind, actionId);
                break;
            default:
                base.OnReceive(context, intent);
                break;
        }
    }

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is not null && Kind(context) is { } kind) Update(context, kind, appWidgetIds);
    }

    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, Android.OS.Bundle? newOptions)
    {
        if (context is not null && Kind(context) is { } kind) Update(context, kind, [appWidgetId]);
    }

    public override void OnDisabled(Context? context)
    {
        if (context is not null && Kind(context) is { } kind) CancelAlarms(context, kind);
    }

    private string? Kind(Context context) => WidgetStore.Kinds(context).ElementAtOrDefault(_index);

    /// <summary>
    /// A refresh alarm: fetches the timeline's remote source when it has one, otherwise runs the provider
    /// through the service, which writes and re-renders.
    /// </summary>
    private void Refresh(Context context, string kind)
    {
        var services = IPlatformApplication.Current?.Services;
        var remote = RemoteSource(context, kind);
        if (remote is null && services is null)
        {
            Update(context, kind);
            return;
        }

        var pending = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                if (remote is { } url) await FetchRemoteAsync(context, kind, url);
                else await services!.GetRequiredService<IWidgetService>().RefreshAsync(kind);
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn(Tag, $"Refreshing widget \"{kind}\" from its alarm failed: {e.Message}");
            }
            finally { pending?.Finish(); }
        });
    }

    /// <summary>A W.Button tap: the provider's handler in the app's process, then the widget rebuilt.</summary>
    private void Tapped(Context context, string kind, string actionId)
    {
        if (IPlatformApplication.Current?.Services is not { } services) return;

        // Now, and not something read off the intent: the receiver runs the moment the button is
        // tapped, so this is the tap's own time.
        var at = DateTimeOffset.Now;
        var pending = GoAsync();
        Task.Run(async () =>
        {
            try { await Extensions.SpineWidgetsExtensions.HandleActionAsync(services, kind, actionId, at); }
            catch (Exception e) { Android.Util.Log.Warn(Tag, $"Action \"{actionId}\" of widget \"{kind}\" failed: {e.Message}"); }
            finally { pending?.Finish(); }
        });
    }

    private static Uri? RemoteSource(Context context, string kind)
    {
        using var document = WidgetStore.ReadTimeline(context, kind);
        return document?.RootElement.TryGetProperty("remote", out var remote) == true
            && Uri.TryCreate(remote.GetString(), UriKind.Absolute, out var url) ? url : null;
    }

    // The fetched document is cached beside the app's own and preferred over it while the source is set;
    // the app's entries are the fallback until the first fetch succeeds.
    private static async Task FetchRemoteAsync(Context context, string kind, Uri url)
    {
        var json = await Http.GetStringAsync(url);
        using (var check = JsonDocument.Parse(json))
            if (!check.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("not a timeline document");

        var target = WidgetStore.RemoteCachePath(context, kind);
        File.WriteAllText(target + ".tmp", json);
        File.Move(target + ".tmp", target, overwrite: true);
        Update(context, kind);
    }

    /// <summary>Draws the entry that applies now into every placed instance of <paramref name="kind"/>, and schedules the next change.</summary>
    internal static void Update(Context context, string kind, int[]? ids = null)
    {
        var index = Array.IndexOf(WidgetStore.Kinds(context), kind);
        if (index < 0 || AppWidgetManager.GetInstance(context) is not { } manager) return;

        var component = new ComponentName(context, Java.Lang.Class.FromType(Slots[index]));
        ids ??= manager.GetAppWidgetIds(component);
        if (ids is not { Length: > 0 }) return;

        var renderer = new RemoteViewsRenderer(context, new WidgetIcons(context), actionId => ButtonIntent(context, index, kind, actionId));
        using var local = WidgetStore.ReadTimeline(context, kind);

        if (local is null)
        {
            foreach (var id in ids) manager.UpdateAppWidget(id, renderer.Root(Placeholder, null));
            // Placed before the app ever built it: ask the provider now rather than waiting for the app.
            context.SendBroadcast(Broadcast(context, index, ActionRefresh, kind));
            return;
        }

        var hasRemote = local.RootElement.TryGetProperty("remote", out var remoteUrl) && remoteUrl.ValueKind == JsonValueKind.String;
        using var remote = hasRemote ? WidgetStore.ReadRemoteCache(context, kind) : null;
        if (hasRemote && remote is null) context.SendBroadcast(Broadcast(context, index, ActionRefresh, kind));

        var now = DateTimeOffset.UtcNow;
        var root = remote?.RootElement ?? local.RootElement;
        var entries = root.GetProperty("entries").EnumerateArray()
            .Select(e => (Date: e.GetProperty("date").GetDateTimeOffset(), Trees: e.GetProperty("trees")))
            .OrderBy(e => e.Date)
            .ToList();
        if (entries.Count == 0) return;

        var current = entries.LastOrDefault(e => e.Date <= now) is { Trees.ValueKind: JsonValueKind.Object } shown ? shown : entries[0];
        var tap = (root.TryGetProperty("link", out var link) || local.RootElement.TryGetProperty("link", out link)) && link.GetString() is { } url
            ? LinkIntent(context, index, url) : null;
        renderer.Background = (root.TryGetProperty("background", out var background) || local.RootElement.TryGetProperty("background", out background))
            && background.ValueKind == JsonValueKind.String ? background.GetString() : null;

        foreach (var id in ids)
            manager.UpdateAppWidget(id, Views(renderer, current.Trees, tap, manager, id));

        var refreshAfter = root.TryGetProperty("refreshAfterSeconds", out var refresh) && refresh.ValueKind == JsonValueKind.Number ? refresh.GetDouble()
            : hasRemote ? MinimumRefreshInterval.TotalSeconds : (double?)null;
        Schedule(context, index, kind, entries.Select(e => e.Date).ToList(), refreshAfter, now);
    }

    private static RemoteViews Views(RemoteViewsRenderer renderer, JsonElement trees, PendingIntent? tap, AppWidgetManager manager, int id)
    {
        var byFamily = trees.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var sized = new Dictionary<Android.Util.SizeF, RemoteViews>();
            foreach (var (name, width, height) in Families)
                if (byFamily.TryGetValue(name, out var tree))
                {
                    renderer.Family = name;
                    sized[new Android.Util.SizeF(width, height)] = renderer.Root(tree, tap);
                }
            if (sized.Count > 0) return new RemoteViews(sized);
            // One tree for every family: let the launcher still pick, so an adaptive node inside it works.
            if (byFamily.TryGetValue(WidgetJson.DefaultFamilyKey, out var shared))
            {
                foreach (var (name, width, height) in Families)
                {
                    renderer.Family = name;
                    sized[new Android.Util.SizeF(width, height)] = renderer.Root(shared, tap);
                }
                return new RemoteViews(sized);
            }
        }

        var options = manager.GetAppWidgetOptions(id);
        var minWidth = options?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth) ?? 0;
        var minHeight = options?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight) ?? 0;
        var family = minWidth >= 250 ? (minHeight >= 250 ? "large" : "medium") : "small";

        var chosen = byFamily.TryGetValue(family, out var exact) ? exact
            : byFamily.TryGetValue(WidgetJson.DefaultFamilyKey, out var fallback) ? fallback
            : byFamily.Values.FirstOrDefault();
        renderer.Family = family;
        return renderer.Root(chosen.ValueKind == JsonValueKind.Object ? chosen : Placeholder, tap);
    }

    /// <summary>
    /// The platform switches entries by itself on iOS; here an inexact alarm re-renders at the next entry's
    /// date, and another runs the provider <c>refreshAfterSeconds</c> after the last one. Both are cheap and
    /// need no exact-alarm permission.
    /// </summary>
    private static void Schedule(Context context, int index, string kind, List<DateTimeOffset> dates, double? refreshAfterSeconds, DateTimeOffset now)
    {
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;

        var render = Alarm(context, index, ActionRender, kind);
        if (dates.FirstOrDefault(d => d > now) is var next && next != default) Set(alarms, next, render);
        else alarms.Cancel(render);

        // A refresh moment already behind us (a provider on a faked clock, a stale document) is
        // pushed out rather than fired at once, so a provider cannot put the app in a tight loop.
        var refresh = Alarm(context, index, ActionRefresh, kind);
        if (refreshAfterSeconds is { } seconds)
        {
            var at = dates[^1].AddSeconds(seconds);
            Set(alarms, at > now ? at : now + MinimumRefreshInterval, refresh);
        }
        else alarms.Cancel(refresh);
    }

    private static void CancelAlarms(Context context, string kind)
    {
        var index = Array.IndexOf(WidgetStore.Kinds(context), kind);
        if (index < 0) return;
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        alarms.Cancel(Alarm(context, index, ActionRender, kind));
        alarms.Cancel(Alarm(context, index, ActionRefresh, kind));
    }

    private static void Set(AlarmManager alarms, DateTimeOffset at, PendingIntent intent)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at.ToUnixTimeMilliseconds(), intent);
        else alarms.Set(AlarmType.RtcWakeup, at.ToUnixTimeMilliseconds(), intent);
    }

    private static Intent Broadcast(Context context, int index, string action, string kind) =>
        new Intent(context, Java.Lang.Class.FromType(Slots[index])).SetAction(action).PutExtra(ExtraKind, kind);

    // Request codes keep the two alarms of a kind apart; the intent itself is what AlarmManager matches on.
    private static PendingIntent Alarm(Context context, int index, string action, string kind) =>
        PendingIntent.GetBroadcast(context, index * 2 + (action == ActionRefresh ? 1 : 0), Broadcast(context, index, action, kind), PendingFlags)!;

    private static PendingIntentFlags PendingFlags =>
        OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable : PendingIntentFlags.UpdateCurrent;

    // One PendingIntent per button; the request code keeps them apart, the extras say which.
    private static PendingIntent? ButtonIntent(Context context, int index, string kind, string actionId) =>
        PendingIntent.GetBroadcast(context, 1000 + index * 100 + (actionId.GetHashCode() & 0x7F),
            Broadcast(context, index, ActionButton, kind).PutExtra(ExtraAction, actionId), PendingFlags);

    /// <summary>The tap: the same URL as on iOS, delivered to the app through <see cref="SpineWidgetLinkActivity"/>.</summary>
    private static PendingIntent? LinkIntent(Context context, int index, string url)
    {
        if (Android.Net.Uri.Parse(url) is not { } uri) return null;
        var intent = new Intent(context, typeof(SpineWidgetLinkActivity)).SetAction(Intent.ActionView).SetData(uri);
        return PendingIntent.GetActivity(context, 100 + index, intent, PendingFlags);
    }
}

[Register("plugin/maui/spine/widgets/SpineAppWidget0")] internal sealed class SpineAppWidget0() : SpineAppWidget(0);
[Register("plugin/maui/spine/widgets/SpineAppWidget1")] internal sealed class SpineAppWidget1() : SpineAppWidget(1);
[Register("plugin/maui/spine/widgets/SpineAppWidget2")] internal sealed class SpineAppWidget2() : SpineAppWidget(2);
[Register("plugin/maui/spine/widgets/SpineAppWidget3")] internal sealed class SpineAppWidget3() : SpineAppWidget(3);
[Register("plugin/maui/spine/widgets/SpineAppWidget4")] internal sealed class SpineAppWidget4() : SpineAppWidget(4);
[Register("plugin/maui/spine/widgets/SpineAppWidget5")] internal sealed class SpineAppWidget5() : SpineAppWidget(5);
[Register("plugin/maui/spine/widgets/SpineAppWidget6")] internal sealed class SpineAppWidget6() : SpineAppWidget(6);
[Register("plugin/maui/spine/widgets/SpineAppWidget7")] internal sealed class SpineAppWidget7() : SpineAppWidget(7);
[Register("plugin/maui/spine/widgets/SpineAppWidget8")] internal sealed class SpineAppWidget8() : SpineAppWidget(8);
