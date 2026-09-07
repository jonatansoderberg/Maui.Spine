using System.Text.Json;
using Android.Content;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Where the app writes and the receiver reads. Both run in the same package, so plain app-internal
/// storage is enough — no shared container as on iOS.
/// </summary>
internal static class WidgetStore
{
    public static string Root(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, "spine-widgets");

    public static string TimelinePath(Context context, string kind) => Path.Combine(Root(context), kind + ".json");

    /// <summary>The last document fetched from the timeline's remote source, when it has one.</summary>
    public static string RemoteCachePath(Context context, string kind) => Path.Combine(Root(context), kind + ".remote.json");

    public static string AssetsDirectory(Context context) => Path.Combine(Root(context), "assets");

    public static JsonDocument? ReadTimeline(Context context, string kind) => Read(TimelinePath(context, kind), kind);

    public static JsonDocument? ReadRemoteCache(Context context, string kind) => Read(RemoteCachePath(context, kind), kind);

    private static JsonDocument? Read(string path, string kind)
    {
        if (!File.Exists(path)) return null;
        try { return JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Android.Util.Log.Warn("SpineWidgets", $"{kind}.json failed to load: {e.Message}");
            return null;
        }
    }

    /// <summary>The kinds the build declared, in the order the receivers were assigned; empty when the targets did not run.</summary>
    public static string[] Kinds(Context context)
    {
        var id = context.Resources!.GetIdentifier("spine_widget_kinds", "array", context.PackageName);
        return id == 0 ? [] : context.Resources.GetStringArray(id) ?? [];
    }
}
