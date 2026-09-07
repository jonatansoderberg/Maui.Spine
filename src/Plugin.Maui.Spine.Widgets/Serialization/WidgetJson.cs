using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Widgets.Serialization;

/// <summary>The document written per widget kind; the native renderer's input.</summary>
internal sealed record WidgetTimelineDocument(
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("refreshAfterSeconds")] double? RefreshAfterSeconds,
    [property: JsonPropertyName("entries")] IReadOnlyList<WidgetTimelineEntryDocument> Entries);

/// <summary>One entry of <see cref="WidgetTimelineDocument"/>; <c>trees</c> is keyed by family name or <c>default</c>.</summary>
internal sealed record WidgetTimelineEntryDocument(
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("trees")] IReadOnlyDictionary<string, WidgetNode> Trees);

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WidgetTimelineDocument))]
[JsonSerializable(typeof(LiveActivityLayout))]
[JsonSerializable(typeof(WidgetNode))]
internal sealed partial class WidgetJsonContext : JsonSerializerContext;

internal static class WidgetJson
{
    public const string DefaultFamilyKey = "default";

    public static string Serialize(WidgetTimeline timeline)
    {
        var entries = timeline.Entries
            .OrderBy(e => e.Date)
            .Select(e => new WidgetTimelineEntryDocument(e.Date, e.Trees is { } trees
                ? trees.ToDictionary(t => FamilyKey(t.Key), t => t.Value)
                : new Dictionary<string, WidgetNode> { [DefaultFamilyKey] = e.Tree! }))
            .ToList();

        var document = new WidgetTimelineDocument(timeline.Link?.ToString(), timeline.RefreshAfter?.TotalSeconds, entries);
        return JsonSerializer.Serialize(document, WidgetJsonContext.Default.WidgetTimelineDocument);
    }

    public static string Serialize(LiveActivityLayout layout) =>
        JsonSerializer.Serialize(layout, WidgetJsonContext.Default.LiveActivityLayout);

    // Family keys are the JSON names of WidgetFamily; the Swift side switches on the same strings.
    public static string FamilyKey(WidgetFamily family) => family switch
    {
        WidgetFamily.Small => "small",
        WidgetFamily.Medium => "medium",
        WidgetFamily.Large => "large",
        WidgetFamily.ExtraLarge => "extraLarge",
        WidgetFamily.AccessoryCircular => "accessoryCircular",
        WidgetFamily.AccessoryRectangular => "accessoryRectangular",
        WidgetFamily.AccessoryInline => "accessoryInline",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
    };
}
