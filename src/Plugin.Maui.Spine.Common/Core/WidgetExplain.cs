using System.Text.Json;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Common.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// What a platform will draw of a tree, and why it differs from the tree: the overrides that apply, what is left out, Spine's
/// own rules for the place, and what the platform cannot draw as written. For a debug log or a test, not for the user.
/// </summary>
public static class WidgetExplain
{
    /// <summary>Every region of the layout on <paramref name="platform"/>, each on its own surface.</summary>
    public static IReadOnlyList<string> Explain(this LiveActivityLayout layout, WidgetPlatform platform)
    {
        var json = JsonNode.Parse(WidgetJson.ResolveShared(layout.ToJson())) as JsonObject ?? [];
        var lines = new List<string>();
        var regions = platform == WidgetPlatform.Android
            ? new[] { ("lockScreen", WidgetPlace.Notification) }
            : [("lockScreen", WidgetPlace.LockScreen), ("expandedLeading", WidgetPlace.Island), ("expandedTrailing", WidgetPlace.Island), ("expandedCenter", WidgetPlace.Island),
               ("expandedBottom", WidgetPlace.Island), ("compactLeading", WidgetPlace.Island), ("compactTrailing", WidgetPlace.Island), ("minimal", WidgetPlace.Island),
               ("small", WidgetPlace.CarPlay | WidgetPlace.Watch)];
        foreach (var (region, surface) in regions)
            if (json[region] is JsonObject tree)
            {
                lines.AddRange(Explain(tree, platform, surface, null).Select(line => $"{region}: {line}"));
                foreach (var twice in Nodes(tree).Select(n => n.Node["part"]?.GetValue<string>()).OfType<string>().GroupBy(p => p).Where(g => g.Count() > 1))
                    lines.Add($"{region}: two nodes are Part {twice.Key}; the first is used");
            }

        var parts = json["lockScreen"] is JsonObject lockScreen ? Nodes(lockScreen).Select(n => n.Node["part"]?.GetValue<string>()).OfType<string>().ToHashSet() : [];
        if (platform == WidgetPlatform.Android && layout.Android is null && !parts.Contains("subject") && !parts.Contains("value"))
            lines.Add("notification: no Notification text and no Subject or Value part, so its title is a guess from the tree's texts");
        return lines;
    }

    /// <summary>One tree drawn on <paramref name="platform"/>, on <paramref name="surface"/>, in a widget of <paramref name="size"/>.</summary>
    public static IReadOnlyList<string> Explain(this WidgetNode tree, WidgetPlatform platform, WidgetPlace surface, WidgetFamily? size = null)
    {
        var json = JsonSerializer.SerializeToNode(tree, WidgetJsonContext.Default.WidgetNode) as JsonObject;
        return json is null ? [] : Explain(json, platform, surface, size is { } family ? WidgetOverrideWhen.JsonName(family.ToString()) : null);
    }

    private static List<string> Explain(JsonObject tree, WidgetPlatform platform, WidgetPlace surface, string? size)
    {
        var lines = new List<string>();
        var place = new DrawPlace(
            platform == WidgetPlatform.Android ? "android" : "ios",
            [.. WidgetOverrideWhen.Names<WidgetPlace>(surface)!.Split(',')],
            size,
            platform == WidgetPlatform.Android ? 17 : 26);

        Hidden(tree, place, "", lines);
        var drawn = WidgetOverrides.Apply(tree, place, (path, node, set) =>
            lines.Add($"{Name(path, node)}: {string.Join(", ", set.Select(p => p.Value is null ? $"no {p.Key}" : $"{p.Key} {p.Value.ToJsonString()}"))} ({place.Platform}, {string.Join('/', place.Surfaces)})"));

        if (platform == WidgetPlatform.Android && surface.HasFlag(WidgetPlace.Notification) && drawn is JsonObject root)
        {
            if (root["padding"] is not null || root["insets"] is not null)
                lines.Add("root: its padding is dropped; the notification insets its content to the header's app name");
            if (Nodes(root).FirstOrDefault(n => n.Node["type"]?.GetValue<string>() is "relative" or "timer") is { Node: not null } clock && clock.Node["hidden"]?.GetValue<bool>() != false)
                lines.Add($"{Name(clock.Path, clock.Node)}: not drawn; the notification's header counts it (.On(WidgetPlace.Notification, n => n.Hidden(false)) keeps it)");
        }
        if (drawn is JsonObject shown)
            foreach (var (path, node) in Nodes(shown))
                foreach (var note in Notes(node, platform))
                    lines.Add($"{Name(path, node)}: {note}");
        return lines;
    }

    private static void Hidden(JsonObject node, DrawPlace place, string path, List<string> lines)
    {
        var applied = WidgetOverrides.Apply(new JsonObject { ["type"] = node["type"]?.DeepClone(), ["hidden"] = node["hidden"]?.DeepClone(), ["on"] = node["on"]?.DeepClone() }, place);
        if (applied is null) { lines.Add($"{Name(path, node)}: left out"); return; }
        if (node["children"] is JsonArray children)
            for (var i = 0; i < children.Count; i++)
                if (children[i] is JsonObject child) Hidden(child, place, $"{path}/{i}", lines);
    }

    private static IEnumerable<string> Notes(JsonObject node, WidgetPlatform platform)
    {
        var type = node["type"]?.GetValue<string>();
        if (platform == WidgetPlatform.Android)
        {
            if (type == "hstack" && node["align"]?.GetValue<string>() == "baseline") yield return "lines up the tops, not the baselines";
            if (node["design"] is not null) yield return "drawn in the default font";
            if (node["shrink"] is not null || (node["singleLine"] is { } single && single.GetValue<double>() < 1)) yield return "kept to its lines, not shrunk to fit";
            if (type == "relative" && (node["ago"] is not null || node["rounded"] is not null)) yield return "drawn as a clock";
            if (type is "relative" or "timer" && node["italic"] is not null) yield return "not in italics (a clock takes no span)";
            if (node["cornerRadius"] is not null) yield return "square below Android 12";
            if (node["pending"] is not null) yield return "not dimmed while the tap is handled (iOS only)";
            if (node["accented"] is not null) yield return "no accent group (iOS only)";
        }
    }

    private static IEnumerable<(string Path, JsonObject Node)> Nodes(JsonObject node, string path = "")
    {
        yield return (path, node);
        if (node["children"] is JsonArray children)
            for (var i = 0; i < children.Count; i++)
                if (children[i] is JsonObject child)
                    foreach (var found in Nodes(child, $"{path}/{i}")) yield return found;
        if (node["child"] is JsonObject inner)
            foreach (var found in Nodes(inner, $"{path}/child")) yield return found;
    }

    private static string Name(string path, JsonObject node)
    {
        var type = node["type"]?.GetValue<string>() ?? "node";
        var text = node["text"]?.GetValue<string>() is { } t ? $" \"{(t.Length > 24 ? t[..24] + "…" : t)}\"" : "";
        return $"{(path.Length == 0 ? "root" : path.TrimStart('/'))} {type}{text}";
    }
}
