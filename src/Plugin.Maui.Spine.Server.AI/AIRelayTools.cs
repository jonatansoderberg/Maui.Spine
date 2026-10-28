using System.Text.Json.Nodes;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// The model's tools in a relayed session, Live or Realtime: the functions the app runs itself as it declares them, as far
/// as the feature's catalogue lists them for the device (<c>end_conversation</c> always), the feature's server functions the
/// app does not run, and the hosted tools named. Says which side runs each server function, and which of the app's
/// functions the catalogue left out.
/// </summary>
internal static class AIRelayTools
{
    public static JsonArray Build(
        AIFeatures features, AIFeature feature, SpineAIOptions options, JsonArray? appTools, IEnumerable<string> hostedTools,
        out Dictionary<string, SpineAIFunction> serverRuns, out List<string> unlisted)
    {
        var list = new JsonArray();
        var appRuns = new HashSet<string>(StringComparer.Ordinal);

        // The app's own tools only as far as the feature lists them and the server declared them for the device: the
        // feature's catalogue decides in every session kind. The session's hang-up is the app's own. What the app offered
        // beyond the catalogue is named to the caller, since a model left without the functions it was told about behaves
        // oddly (a backend with only end_conversation hung up on every question, 2026-10-28).
        var listed = features.FunctionsOf(feature).Where(t => t.OnDevice).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        unlisted = [];
        foreach (var tool in appTools?.OfType<JsonObject>() ?? [])
        {
            if (Text(tool, "name") is not { Length: > 0 } name) continue;
            if (!(listed.Contains(name) || name == SpineAIFunctions.EndConversationName))
            {
                unlisted.Add(name);
                continue;
            }
            if (!appRuns.Add(name)) continue;
            list.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = name,
                ["description"] = Text(tool, "description") ?? "",
                ["parameters"] = tool["parameters"] is JsonObject parameters ? parameters.DeepClone() : EmptySchema(),
            });
        }

        serverRuns = new Dictionary<string, SpineAIFunction>(StringComparer.Ordinal);
        foreach (var tool in features.FunctionsOf(feature).Where(t => t.OnServer && !appRuns.Contains(t.Name)))
        {
            if (options.Functions.ServerFunction(tool.Name) is not { } declared) continue;
            serverRuns[tool.Name] = declared;
            list.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText()),
            });
        }

        foreach (var hosted in hostedTools)
            list.Add(new JsonObject { ["type"] = hosted });

        return list;
    }

    private static JsonObject EmptySchema() => new() { ["type"] = "object", ["properties"] = new JsonObject() };

    private static string? Text(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
