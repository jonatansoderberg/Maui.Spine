using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Plugin.Maui.Spine.Common.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// A Live Activity's layout with its bound values left out: what the app installs on the device once
/// (<see cref="SpineWidgetsOptions.Templates"/>), so a push carries only the values
/// (<see cref="LiveActivityTemplateData"/>). Written by a layout file with <c>Payload="Template"</c>, as its generated
/// <c>Template</c>; the device fills it in, on iOS in the widget extension, on Android in the app.
/// </summary>
/// <remarks>
/// The document is the layout's JSON with <c>{"$":"key"}</c> where a value goes, <c>{"type":"$if"}</c> nodes where the
/// data decides, <c>{"type":"$each"}</c> among children for a list, and <c>{"type":"$"}</c> for a node the data holds
/// whole. Every value is worked out by the app's own code before it is sent, formatted and in the user's language, so
/// filling it in only puts values in place and picks branches.
/// </remarks>
public sealed class LiveActivityTemplate
{
    private readonly JsonObject _root;

    private LiveActivityTemplate(string id, JsonObject root, IReadOnlyList<string>? icons = null)
    {
        Id = id;
        _root = root;
        Json = root.ToJsonString();
        Icons = [.. (icons ?? []).Concat(root.DescendantsAndSelf().OfType<JsonObject>()
            .Select(n => n["systemImage"] is JsonValue v && v.TryGetValue<string>(out var name) ? name : null).OfType<string>()).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The template's name and a hash of its content, <c>glucose-activity@1a2b3c4d</c>: a changed layout is a new template.</summary>
    public string Id { get; }

    /// <summary>The template document.</summary>
    public string Json { get; }

    /// <summary>Every icon the template can show, drawn when the app installs it so a pushed value finds its icon.</summary>
    public IReadOnlyList<string> Icons { get; }

    /// <summary>The layout <paramref name="data"/> fills in, as JSON: what <c>Build(model)</c> writes for the same model.</summary>
    public string Render(LiveActivityTemplateData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.TemplateId != Id)
            throw new ArgumentException($"The data is for {data.TemplateId}, not {Id}.", nameof(data));
        return LayoutTemplates.Render(_root, data.Data).ToJsonString();
    }

    /// <summary>The layout <paramref name="data"/> fills in.</summary>
    public LiveActivityLayout RenderLayout(LiveActivityTemplateData data) => WidgetJson.DeserializeLayout(Render(data))!;

    /// <summary>Reads a template the app installed, as <see cref="Json"/> wrote it.</summary>
    public static LiveActivityTemplate Parse(string id, string json) =>
        new(id, JsonNode.Parse(json) as JsonObject ?? throw new JsonException("A template is a JSON object."));

    /// <summary>
    /// Builds the template from the generated code: <paramref name="build"/> writes the layout with the builder's
    /// placeholders where the data goes. Called by a layout file's generated <c>Template</c>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static LiveActivityTemplate Create(string name, IReadOnlyList<string> icons, Func<LiveActivityTemplateBuilder, LiveActivityLayout> build)
    {
        var builder = new LiveActivityTemplateBuilder();
        var root = JsonNode.Parse(build(builder).ToJson()) as JsonObject ?? [];
        var document = (JsonObject)builder.Replace(root)!;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document.ToJsonString())))[..8];
        return new($"{name}@{hash}", document, icons);
    }
}

/// <summary>The values for one <see cref="LiveActivityTemplate"/>: what a push carries instead of the whole layout.</summary>
/// <param name="TemplateId">The template's <see cref="LiveActivityTemplate.Id"/>.</param>
/// <param name="Data">The values by key.</param>
public sealed record LiveActivityTemplateData(string TemplateId, JsonObject Data)
{
    /// <summary>The data as JSON, as a push carries it under <c>d</c>.</summary>
    public string ToJson() => Data.ToJsonString();
}

/// <summary>The placeholders the generated <c>Template</c> writes where a layout file binds a value. Not meant to be called by hand.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LiveActivityTemplateBuilder
{
    private const char Mark = '\u0001';
    private const string Scheme = "spine-template";
    private const double Base = 1e15;
    private readonly Dictionary<double, string> _numbers = [];
    private readonly Dictionary<string, string> _dates = new(StringComparer.Ordinal);

    internal LiveActivityTemplateBuilder() { }

    /// <summary>A bound text or name.</summary>
    public string S(string key) => Mark + key;

    /// <summary>A bound number.</summary>
    public double N(string key)
    {
        var value = Base + _numbers.Count;
        _numbers[value] = key;
        return value;
    }

    /// <summary>A bound list of numbers, as a graph's values.</summary>
    public IReadOnlyList<double?> L(string key) => [N(key)];

    /// <summary>A bound color.</summary>
    public WidgetColor C(string key) => WidgetColor.Placeholder(Mark + key);

    /// <summary>A bound moment.</summary>
    public DateTimeOffset D(string key)
    {
        var value = DateTimeOffset.UnixEpoch.AddSeconds(_dates.Count + 1);
        _dates[value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)] = key;
        return value;
    }

    /// <summary>A bound link.</summary>
    public Uri U(string key) => new($"{Scheme}:{Uri.EscapeDataString(key)}");

    /// <summary>A node shown when the data's <paramref name="key"/> is true, else <paramref name="otherwise"/>.</summary>
    public WidgetNode If(string key, WidgetNode? then, WidgetNode? otherwise = null) => new TemplateIfNode(key, then, otherwise);

    /// <summary>The nodes for each item of the data's list <paramref name="key"/>, or <paramref name="empty"/> when it has none.</summary>
    public WidgetNode Each(string key, WidgetNode?[] item, WidgetNode?[]? empty = null) =>
        new TemplateEachNode(key, [.. item.OfType<WidgetNode>()], empty is null ? null : [.. empty.OfType<WidgetNode>()]);

    /// <summary>A node the data holds whole.</summary>
    public WidgetNode Node(string key) => new TemplateDataNode(key);

    internal JsonNode? Replace(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (name, value) in obj)
                    copy[name] = Replace(value);
                return copy;
            case JsonArray array:
                if (array.Count == 1 && array[0] is JsonValue only && only.TryGetValue<double>(out var listNumber) && _numbers.TryGetValue(listNumber, out var listKey))
                    return Placeholder(listKey);
                return new JsonArray([.. array.Select(Replace)]);
            case JsonValue value when value.TryGetValue<string>(out var text):
                if (text.Length > 1 && text[0] == Mark) return Placeholder(text[1..]);
                if (text.StartsWith(Scheme + ":", StringComparison.Ordinal)) return Placeholder(Uri.UnescapeDataString(text[(Scheme.Length + 1)..]));
                if (_dates.TryGetValue(text, out var dateKey)) return Placeholder(dateKey);
                return value.DeepClone();
            case JsonValue value when value.TryGetValue<double>(out var number) && _numbers.TryGetValue(number, out var numberKey):
                return Placeholder(numberKey);
            default:
                return node?.DeepClone();
        }
    }

    private static JsonObject Placeholder(string key) => new() { ["$"] = key };
}

/// <summary>The values the generated <c>Data(model)</c> collects, by key. Not meant to be called by hand.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LiveActivityTemplateValues
{
    internal JsonObject Json { get; } = [];

    /// <summary>A text; none is left out.</summary>
    public void Set(string key, string? value) { if (value is not null) Json[key] = value; }

    /// <summary>A number; none is left out.</summary>
    public void Set(string key, double? value) { if (value is { } number) Json[key] = number; }

    /// <summary>A color; none is left out.</summary>
    public void Set(string key, WidgetColor? value) { if (value is { } color) Json[key] = color.Value; }

    /// <summary>A moment, written as the layout writes it.</summary>
    public void Set(string key, DateTimeOffset? value) { if (value is { } date) Json[key] = JsonValue.Create(date.UtcDateTime); }

    /// <summary>A link.</summary>
    public void Set(string key, Uri? value) { if (value is not null) Json[key] = value.ToString(); }

    /// <summary>A list of numbers: a graph's values.</summary>
    public void Set(string key, IReadOnlyList<double?>? values) { if (values is not null) Json[key] = new JsonArray([.. values.Select(v => v is { } d ? JsonValue.Create(d) : null)]); }

    /// <summary>A whole node.</summary>
    public void Set(string key, WidgetNode? node) { if (node is not null) Json[key] = JsonSerializer.SerializeToNode(node, WidgetJsonContext.Default.WidgetNode); }

    /// <summary>Records a condition and returns it, so the generated code can follow it with the values of that branch alone.</summary>
    public bool If(string key, bool value)
    {
        if (value) Json[key] = true;
        return value;
    }

    /// <summary>The values of each of at most <paramref name="max"/> items, under <paramref name="key"/>.</summary>
    public bool Each<T>(string key, IEnumerable<T>? items, int? max, Action<LiveActivityTemplateValues, T> item)
    {
        var list = new JsonArray();
        foreach (var value in max is { } count ? (items ?? []).Take(count) : items ?? [])
        {
            var values = new LiveActivityTemplateValues();
            item(values, value);
            list.Add(values.Json);
        }
        if (list.Count > 0) Json[key] = list;
        return list.Count > 0;
    }

    /// <summary>The data for <paramref name="template"/>.</summary>
    public LiveActivityTemplateData For(LiveActivityTemplate template) => new(template.Id, Json);
}

/// <summary>A branch the data picks.</summary>
internal sealed record TemplateIfNode(
    [property: JsonPropertyName("test")] string Test,
    [property: JsonPropertyName("then")] WidgetNode? Then,
    [property: JsonPropertyName("else")] WidgetNode? Else) : WidgetNode;

/// <summary>A list the data holds, among a stack's children.</summary>
internal sealed record TemplateEachNode(
    [property: JsonPropertyName("items")] string Items,
    [property: JsonPropertyName("do")] IReadOnlyList<WidgetNode> Do,
    [property: JsonPropertyName("empty")] IReadOnlyList<WidgetNode>? Empty) : WidgetNode;

/// <summary>A node the data holds whole.</summary>
internal sealed record TemplateDataNode([property: JsonPropertyName("key")] string Key) : WidgetNode;

/// <summary>
/// Fills a template in: the same steps as the widget extension's <c>Templates</c> in Swift. A placeholder takes the
/// data's value, or leaves its property out when there is none; a <c>$if</c> takes its branch; a <c>$each</c> repeats its
/// nodes for each item, the item's values before the outer ones.
/// </summary>
internal static class LayoutTemplates
{
    public static IEnumerable<JsonNode> DescendantsAndSelf(this JsonNode node) =>
        node switch
        {
            JsonObject obj => obj.Select(p => p.Value).OfType<JsonNode>().SelectMany(DescendantsAndSelf).Prepend(node),
            JsonArray array => array.OfType<JsonNode>().SelectMany(DescendantsAndSelf).Prepend(node),
            _ => [node],
        };

    public static JsonNode Render(JsonObject template, JsonObject data)
    {
        var layout = Fill(template, [data]) as JsonObject ?? [];
        if (layout["shared"] is JsonObject { Count: 0 })
            layout.Remove("shared");
        return layout;
    }

    private static JsonNode? Fill(JsonNode? node, List<JsonObject> scopes)
    {
        switch (node)
        {
            case JsonObject obj when obj.Count == 1 && obj["$"] is JsonValue key:
                return Lookup(key.GetValue<string>(), scopes)?.DeepClone();
            case JsonObject obj when Type(obj) == "$if":
                return Fill(Truthy(Lookup(Text(obj, "test"), scopes)) ? obj["then"] : obj["else"], scopes);
            case JsonObject obj when Type(obj) == "$":
                return Lookup(Text(obj, "key"), scopes)?.DeepClone();
            case JsonObject obj:
                var filled = new JsonObject();
                foreach (var (name, value) in obj)
                    if (Fill(value, scopes) is { } child)
                        filled[name] = child;
                return filled;
            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    if (item is JsonObject each && Type(each) == "$each")
                    {
                        var list = Lookup(Text(each, "items"), scopes) as JsonArray;
                        if (list is { Count: > 0 })
                            foreach (var entry in list.OfType<JsonObject>())
                                Splice(items, each["do"], [entry, .. scopes]);
                        else
                            Splice(items, each["empty"], scopes);
                        continue;
                    }
                    if (Fill(item, scopes) is { } child)
                        items.Add(child);
                }
                return items;
            default:
                return node?.DeepClone();
        }
    }

    private static void Splice(JsonArray into, JsonNode? nodes, List<JsonObject> scopes)
    {
        if (Fill(nodes, scopes) is JsonArray filled)
            foreach (var node in filled.ToList())
            {
                filled.Remove(node);
                into.Add(node);
            }
    }

    private static JsonNode? Lookup(string key, List<JsonObject> scopes) =>
        scopes.FirstOrDefault(s => s.ContainsKey(key)) is { } scope ? scope[key] : null;

    private static bool Truthy(JsonNode? value) => value is JsonValue v && v.TryGetValue<bool>(out var flag) && flag;

    private static string? Type(JsonObject node) => node["type"] is JsonValue v && v.TryGetValue<string>(out var type) ? type : null;

    private static string Text(JsonObject node, string key) => node[key]?.GetValue<string>() ?? "";
}
