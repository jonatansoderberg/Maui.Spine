using System.Text.Json;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Runtime;
using Android.Widget;
using Microsoft.Extensions.Logging;
using Plugin.Maui.SvgImage;

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
    private const string ExtraKind = "plugin.maui.spine.widgets.KIND";

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

    /// <summary>Runs the provider for <paramref name="kind"/> when a refresh alarm fires; the service writes and re-renders.</summary>
    private void Refresh(Context context, string kind)
    {
        if (IPlatformApplication.Current?.Services is not { } services)
        {
            Update(context, kind);
            return;
        }

        var pending = GoAsync();
        Task.Run(async () =>
        {
            try { await services.GetRequiredService<IWidgetService>().RefreshAsync(kind); }
            catch (Exception e) { services.GetRequiredService<ILogger<IWidgetService>>().LogError(e, "Refreshing widget \"{Kind}\" from its alarm failed.", kind); }
            finally { pending?.Finish(); }
        });
    }

    /// <summary>Draws the entry that applies now into every placed instance of <paramref name="kind"/>, and schedules the next change.</summary>
    internal static void Update(Context context, string kind, int[]? ids = null)
    {
        var index = Array.IndexOf(WidgetStore.Kinds(context), kind);
        if (index < 0 || AppWidgetManager.GetInstance(context) is not { } manager) return;

        var component = new ComponentName(context, Java.Lang.Class.FromType(Slots[index]));
        ids ??= manager.GetAppWidgetIds(component);
        if (ids is not { Length: > 0 }) return;

        var renderer = new RemoteViewsRenderer(context, new WidgetIcons(SvgResources()));
        using var document = WidgetStore.ReadTimeline(context, kind);

        if (document is null)
        {
            foreach (var id in ids) manager.UpdateAppWidget(id, renderer.Root(Placeholder, null));
            // Placed before the app ever built it: ask the provider now rather than waiting for the app.
            context.SendBroadcast(Broadcast(context, index, ActionRefresh, kind));
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var root = document.RootElement;
        var entries = root.GetProperty("entries").EnumerateArray()
            .Select(e => (Date: e.GetProperty("date").GetDateTimeOffset(), Trees: e.GetProperty("trees")))
            .OrderBy(e => e.Date)
            .ToList();
        if (entries.Count == 0) return;

        var current = entries.LastOrDefault(e => e.Date <= now) is { Trees.ValueKind: JsonValueKind.Object } shown ? shown : entries[0];
        var tap = root.TryGetProperty("link", out var link) && link.GetString() is { } url ? LinkIntent(context, index, url) : null;

        foreach (var id in ids)
            manager.UpdateAppWidget(id, Views(renderer, current.Trees, tap, manager, id));

        Schedule(context, index, kind, entries.Select(e => e.Date).ToList(),
            root.TryGetProperty("refreshAfterSeconds", out var refresh) && refresh.ValueKind == JsonValueKind.Number ? refresh.GetDouble() : null, now);
    }

    private static RemoteViews Views(RemoteViewsRenderer renderer, JsonElement trees, PendingIntent? tap, AppWidgetManager manager, int id)
    {
        var byFamily = trees.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var sized = new Dictionary<Android.Util.SizeF, RemoteViews>();
            foreach (var (name, width, height) in Families)
                if (byFamily.TryGetValue(name, out var tree))
                    sized[new Android.Util.SizeF(width, height)] = renderer.Root(tree, tap);
            if (sized.Count > 0) return new RemoteViews(sized);
        }

        var options = manager.GetAppWidgetOptions(id);
        var minWidth = options?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth) ?? 0;
        var minHeight = options?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight) ?? 0;
        var family = minWidth >= 250 ? (minHeight >= 250 ? "large" : "medium") : "small";

        var chosen = byFamily.TryGetValue(family, out var exact) ? exact
            : byFamily.TryGetValue(Serialization.WidgetJson.DefaultFamilyKey, out var fallback) ? fallback
            : byFamily.Values.FirstOrDefault();
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

    /// <summary>The tap: the same URL as on iOS, delivered to the app through <see cref="SpineWidgetLinkActivity"/>.</summary>
    private static PendingIntent? LinkIntent(Context context, int index, string url)
    {
        if (Android.Net.Uri.Parse(url) is not { } uri) return null;
        var intent = new Intent(context, typeof(SpineWidgetLinkActivity)).SetAction(Intent.ActionView).SetData(uri);
        return PendingIntent.GetActivity(context, 100 + index, intent, PendingFlags);
    }

    // The receiver runs inside the app's process, so the registry UseSpine filled is already there; the
    // instance only matters as a handle to its static map.
    private static ResourceNameCache SvgResources() =>
        IPlatformApplication.Current?.Services.GetService<ResourceNameCache>() ?? new ResourceNameCache();
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
