using System.Text.Json.Nodes;

namespace Plugin.Maui.Spine.Common.Serialization;

/// <summary>Where a tree is drawn, for the overrides that apply there: what a renderer knows of the place it draws in.</summary>
/// <param name="Platform"><c>ios</c> or <c>android</c>.</param>
/// <param name="Surfaces">The surface or surfaces (a Live Activity's <c>Small</c> is both CarPlay and the watch).</param>
/// <param name="Size">The widget family's JSON name, or <see langword="null"/> outside a widget.</param>
/// <param name="Version">The OS's major version.</param>
internal sealed record DrawPlace(string Platform, IReadOnlyCollection<string> Surfaces, string? Size, int Version);

/// <summary>
/// Applies a tree's overrides (<see cref="WidgetOverride"/>) for one place, as the Swift renderer's <c>Overrides</c> does: in
/// order, each a merge patch of the node's own properties, and a node left <c>hidden</c> taken out with its children.
/// Android renders from the result; <see cref="WidgetExplain"/> reports from it.
/// </summary>
internal static class WidgetOverrides
{
    /// <summary>The tree as drawn at <paramref name="place"/>, without its <c>on</c> keys; <see langword="null"/> when its root is hidden.</summary>
    public static JsonNode? Apply(JsonNode? tree, DrawPlace place, Action<string, JsonObject, JsonObject>? applied = null) =>
        Walk(tree, place, applied, "");

    private static JsonNode? Walk(JsonNode? value, DrawPlace place, Action<string, JsonObject, JsonObject>? applied, string path)
    {
        if (value is not JsonObject source) return value?.DeepClone();
        var node = (JsonObject)source.DeepClone();
        if (node["on"] is JsonArray overrides)
        {
            node.Remove("on");
            foreach (var entry in overrides.OfType<JsonObject>())
                if (entry["when"] is JsonObject when && Matches(when, place) && entry["set"] is JsonObject set)
                {
                    foreach (var (key, patch) in set)
                        if (patch is null) node.Remove(key);
                        else node[key] = patch.DeepClone();
                    applied?.Invoke(path, node, set);
                }
        }
        if (node["hidden"]?.GetValue<bool>() == true) return null;

        if (node["children"] is JsonArray children)
            node["children"] = new JsonArray([.. children.Select((child, i) => Walk(child, place, applied, $"{path}/{i}")).Where(child => child is not null)]);
        foreach (var key in new[] { "child", "fallback" })
            if (node[key] is JsonObject inner)
                node[key] = Walk(inner, place, applied, $"{path}/{key}");
        if (node["trees"] is JsonObject trees)
            node["trees"] = new JsonObject(trees.Select(t => KeyValuePair.Create(t.Key, Walk(t.Value, place, applied, $"{path}/{t.Key}"))));
        return node;
    }

    /// <summary>Whether every condition <paramref name="when"/> sets holds at <paramref name="place"/>; a comma means either.</summary>
    public static bool Matches(JsonObject when, DrawPlace place)
    {
        static IEnumerable<string> List(JsonNode? value) =>
            value?.GetValue<string>().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];

        if (when["platform"] is { } platform && !List(platform).Contains(place.Platform, StringComparer.OrdinalIgnoreCase)) return false;
        if (when["surface"] is { } surface && !List(surface).Intersect(place.Surfaces, StringComparer.OrdinalIgnoreCase).Any()) return false;
        if (when["size"] is { } size && (place.Size is null || !List(size).Contains(place.Size, StringComparer.OrdinalIgnoreCase))) return false;
        if (when["minVersion"]?.GetValue<int>() is { } version && place.Version < version) return false;
        return true;
    }

    /// <summary>Whether <paramref name="node"/> has an override for <paramref name="place"/> that sets <paramref name="key"/>.</summary>
    public static bool Sets(JsonObject node, string key, DrawPlace place) =>
        node["on"] is JsonArray overrides && overrides.OfType<JsonObject>()
            .Any(o => o["when"] is JsonObject when && Matches(when, place) && o["set"] is JsonObject set && set.ContainsKey(key));
}
