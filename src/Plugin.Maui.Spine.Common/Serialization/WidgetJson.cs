using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common.Serialization;

/// <summary>The document written per widget kind; the native renderer's input.</summary>
internal sealed record WidgetTimelineDocument(
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("remote")] string? Remote,
    [property: JsonPropertyName("refreshAfterSeconds")] double? RefreshAfterSeconds,
    [property: JsonPropertyName("background")] WidgetColor? Background,
    [property: JsonPropertyName("backgroundGradient")] WidgetGradient? BackgroundGradient,
    [property: JsonPropertyName("backgroundImage")] string? BackgroundImage,
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
[JsonSerializable(typeof(IReadOnlyDictionary<string, WidgetNode>))]
internal sealed partial class WidgetJsonContext : JsonSerializerContext;

/// <summary>
/// The wire format between C# and the platform renderers. Both the widget extension on iOS and
/// <c>RemoteViewsRenderer</c> on Android read exactly what this writes, so a server can build the
/// same documents without either.
/// </summary>
public static class WidgetJson
{
    /// <summary>The key a tree is filed under when it serves every widget family.</summary>
    public const string DefaultFamilyKey = "default";

    /// <summary>The timeline as the renderer's JSON, entries ordered by date.</summary>
    public static string Serialize(WidgetTimeline timeline)
    {
        var entries = timeline.Entries
            .OrderBy(e => e.Date)
            .Select(e => new WidgetTimelineEntryDocument(e.Date, e.Trees is { } trees
                ? trees.ToDictionary(t => FamilyKey(t.Key), t => t.Value)
                : new Dictionary<string, WidgetNode> { [DefaultFamilyKey] = e.Tree! }))
            .ToList();

        var document = new WidgetTimelineDocument(timeline.Link?.ToString(), timeline.Remote?.ToString(), timeline.RefreshAfter?.TotalSeconds, timeline.BackgroundColor, timeline.BackgroundGradient, timeline.BackgroundAsset, entries);
        return JsonSerializer.Serialize(document, WidgetJsonContext.Default.WidgetTimelineDocument);
    }

    /// <summary>The layout as the renderer's JSON — the <c>content-state</c> of a Live Activity push.</summary>
    public static string Serialize(LiveActivityLayout layout) =>
        JsonSerializer.Serialize(layout, WidgetJsonContext.Default.LiveActivityLayout);

    // Family keys are the JSON names of WidgetFamily; the Swift side switches on the same strings.
    /// <summary>
    /// Reads a layout back from the JSON <see cref="Serialize(LiveActivityLayout)"/> wrote. Spine.Push
    /// uses it on Android, where a Live Update arrives as serialized layout in a data message and is
    /// rendered in the app's own process.
    /// </summary>
    /// <param name="json">The serialized layout.</param>
    /// <returns>The layout, or <see langword="null"/> when the JSON was <c>null</c>.</returns>
    /// <exception cref="JsonException">The JSON is not a layout.</exception>
    public static LiveActivityLayout? DeserializeLayout(string json) =>
        JsonSerializer.Deserialize(json, WidgetJsonContext.Default.LiveActivityLayout);

    /// <summary>The JSON name of <paramref name="family"/>; the Swift side switches on the same strings.</summary>
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
