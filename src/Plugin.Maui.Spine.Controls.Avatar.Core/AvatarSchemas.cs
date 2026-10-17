using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>
/// The JSON schemas of format 1.0, embedded in the assembly, and the check that applies them to a
/// package's <c>avatar.json</c> and bindings files. The check covers the keywords the two schemas use
/// (type, enum, const, properties, required, additionalProperties, patternProperties, propertyNames,
/// items, sizes, ranges, patterns, $ref, anyOf, allOf, if/then); it is not a general validator.
/// </summary>
/// <remarks>
/// A key the schema does not know is a warning, not a failure, so a file written for a newer 1.x
/// still loads; everything else the schema rejects fails the package.
/// </remarks>
public static class AvatarSchemas
{
    /// <summary>The resource name of <c>avatar.schema.json</c>.</summary>
    public const string ManifestResourceName = "Plugin.Maui.Spine.Controls.Avatar.Core.Schemas.avatar.schema.json";
    /// <summary>The resource name of <c>bindings.schema.json</c>.</summary>
    public const string BindingsResourceName = "Plugin.Maui.Spine.Controls.Avatar.Core.Schemas.bindings.schema.json";

    // Extension keys files from before the schema wrote at the top level of a bindings file.
    private static readonly HashSet<string> FormerTopLevelExtensions = ["lighting", "bloom", "springs", "neckChain", "blinkFrames"];

    private static readonly Lazy<JsonDocument> s_manifest = new(() => Load(ManifestResourceName));
    private static readonly Lazy<JsonDocument> s_bindings = new(() => Load(BindingsResourceName));
    private static readonly Dictionary<string, Regex> s_patterns = new(StringComparer.Ordinal);

    /// <summary><c>avatar.schema.json</c>.</summary>
    public static JsonElement Manifest => s_manifest.Value.RootElement;

    /// <summary><c>bindings.schema.json</c>.</summary>
    public static JsonElement Bindings => s_bindings.Value.RootElement;

    /// <summary>Opens an embedded schema by its resource name, for a tool that writes it out.</summary>
    public static Stream Open(string resourceName) =>
        typeof(AvatarSchemas).Assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"{resourceName} is not embedded in {typeof(AvatarSchemas).Assembly.GetName().Name}");

    /// <summary>Checks <paramref name="utf8"/> against <paramref name="schema"/>; a JSON syntax error is left to the reader, which names it.</summary>
    public static void Check(JsonElement schema, ReadOnlySpan<byte> utf8, string file, string area, AvatarValidationReport report)
    {
        JsonDocument document;
        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 64 });
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var issues = new List<(string Path, string Message, bool Unknown)>();
            Validate(schema, schema, document.RootElement, "", issues);
            foreach (var (path, message, unknown) in issues)
            {
                var where = path.Length == 0 ? file : $"{file} {path}";
                if (unknown)
                    report.Warn(area, "schema", $"{where}: {message}");
                else
                    report.Fail(area, "schema", $"{where}: {message}");
            }
            if (issues.Count == 0)
                report.Pass(area, "schema", $"{file} matches {(schema.TryGetProperty("title", out var title) ? title.GetString() : "the schema")}");
        }
    }

    // Collects issues when issues is not null; returns whether the value is valid, unknown keys aside.
    private static bool Validate(JsonElement root, JsonElement schema, JsonElement value, string path, List<(string, string, bool)>? issues)
    {
        if (schema.ValueKind == JsonValueKind.True)
            return true;
        if (schema.TryGetProperty("$ref", out var reference))
            schema = Resolve(root, reference.GetString()!);

        var valid = true;
        void Fail(string message)
        {
            valid = false;
            issues?.Add((path, message, false));
        }

        if (schema.TryGetProperty("type", out var type) && !Is(value, type))
        {
            Fail($"is {Describe(value)}, expected {(type.ValueKind == JsonValueKind.Array ? string.Join(" or ", type.EnumerateArray().Select(t => t.GetString())) : type.GetString())}");
            return false;
        }
        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, value))
            Fail($"is {value.GetRawText()}, expected {constant.GetRawText()}");
        if (schema.TryGetProperty("enum", out var options) && !options.EnumerateArray().Any(o => JsonElement.DeepEquals(o, value)))
            Fail($"is {value.GetRawText()}, expected one of {string.Join(", ", options.EnumerateArray().Select(o => o.GetRawText()))}");

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                if (!ValidateObject(root, schema, value, path, issues, Fail))
                    valid = false;
                break;
            case JsonValueKind.Array:
                var count = value.GetArrayLength();
                if (schema.TryGetProperty("minItems", out var minItems) && count < minItems.GetInt32())
                    Fail($"has {count} items, at least {minItems.GetInt32()} required");
                if (schema.TryGetProperty("maxItems", out var maxItems) && count > maxItems.GetInt32())
                    Fail($"has {count} items, at most {maxItems.GetInt32()} allowed");
                if (schema.TryGetProperty("uniqueItems", out var unique) && unique.GetBoolean()
                    && value.EnumerateArray().Select(v => v.GetRawText()).Distinct().Count() != count)
                    Fail("has repeated items");
                if (schema.TryGetProperty("items", out var items))
                {
                    var i = 0;
                    foreach (var item in value.EnumerateArray())
                        valid &= Validate(root, items, item, $"{path}[{i++}]", issues);
                }
                break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                if (schema.TryGetProperty("minLength", out var minLength) && text.Length < minLength.GetInt32())
                    Fail($"is shorter than {minLength.GetInt32()} characters");
                if (schema.TryGetProperty("maxLength", out var maxLength) && text.Length > maxLength.GetInt32())
                    Fail($"is longer than {maxLength.GetInt32()} characters");
                if (schema.TryGetProperty("pattern", out var pattern) && !Pattern(pattern.GetString()!).IsMatch(text))
                    Fail($"'{text}' does not match {pattern.GetString()}");
                break;
            case JsonValueKind.Number:
                var number = value.GetDouble();
                if (schema.TryGetProperty("minimum", out var minimum) && number < minimum.GetDouble())
                    Fail($"{number.ToString(CultureInfo.InvariantCulture)} is below {minimum.GetRawText()}");
                if (schema.TryGetProperty("exclusiveMinimum", out var exclusive) && number <= exclusive.GetDouble())
                    Fail($"{number.ToString(CultureInfo.InvariantCulture)} must be above {exclusive.GetRawText()}");
                if (schema.TryGetProperty("maximum", out var maximum) && number > maximum.GetDouble())
                    Fail($"{number.ToString(CultureInfo.InvariantCulture)} is above {maximum.GetRawText()}");
                break;
        }

        if (schema.TryGetProperty("anyOf", out var anyOf) && !anyOf.EnumerateArray().Any(s => Validate(root, s, value, path, null)))
        {
            // Report against the first alternative, which the schemas order as the common case.
            var first = new List<(string, string, bool)>();
            Validate(root, anyOf[0], value, path, first);
            Fail($"matches none of the {anyOf.GetArrayLength()} allowed forms{(first.Count > 0 ? $" (as the first: {first[0].Item1} {first[0].Item2})" : "")}");
        }
        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var part in allOf.EnumerateArray())
                valid &= Validate(root, part, value, path, issues);
        }
        if (schema.TryGetProperty("if", out var condition) && Validate(root, condition, value, path, null) && schema.TryGetProperty("then", out var then))
            valid &= Validate(root, then, value, path, issues);

        return valid;
    }

    private static bool ValidateObject(JsonElement root, JsonElement schema, JsonElement value, string path, List<(string, string, bool)>? issues, Action<string> fail)
    {
        var valid = true;
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
            {
                if (!value.TryGetProperty(name, out _))
                    fail($"'{name}' is required");
            }
        }

        schema.TryGetProperty("properties", out var properties);
        schema.TryGetProperty("patternProperties", out var patternProperties);
        var hasAdditional = schema.TryGetProperty("additionalProperties", out var additional);
        schema.TryGetProperty("propertyNames", out var propertyNames);

        foreach (var property in value.EnumerateObject())
        {
            var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
            if (propertyNames.ValueKind == JsonValueKind.Object && propertyNames.TryGetProperty("pattern", out var namePattern) && !Pattern(namePattern.GetString()!).IsMatch(property.Name))
                fail($"key '{property.Name}' does not match {namePattern.GetString()}");

            var matched = false;
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                matched = true;
                valid &= Validate(root, propertySchema, property.Value, childPath, issues);
            }
            if (patternProperties.ValueKind == JsonValueKind.Object)
            {
                foreach (var pattern in patternProperties.EnumerateObject())
                {
                    if (Pattern(pattern.Name).IsMatch(property.Name))
                    {
                        matched = true;
                        valid &= Validate(root, pattern.Value, property.Value, childPath, issues);
                    }
                }
            }
            if (matched || !hasAdditional)
                continue;

            if (additional.ValueKind == JsonValueKind.False)
            {
                if (issues is null)
                    fail("");
                else if (path.Length == 0 && FormerTopLevelExtensions.Contains(property.Name) && root.GetProperty("$id").GetString()!.EndsWith("bindings.schema.json", StringComparison.Ordinal))
                    issues.Add((childPath, $"is not a key of the schema; write it as extensions.\"{AvatarBindings.SpineExtensionPrefix}{property.Name}\"", true));
                else
                    issues.Add((childPath, "is not a key of the schema; it is ignored", true));
            }
            else if (additional.ValueKind == JsonValueKind.Object)
                valid &= Validate(root, additional, property.Value, childPath, issues);
        }
        return valid;
    }

    private static bool Is(JsonElement value, JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.Array)
            return type.EnumerateArray().Any(t => Is(value, t));
        return type.GetString() switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d) && d == decimal.Truncate(d),
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => false,
        };
    }

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => $"the string \"{value.GetString()}\"",
        JsonValueKind.Number => $"the number {value.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => "null",
    };

    private static JsonElement Resolve(JsonElement root, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            throw new NotSupportedException($"$ref '{reference}': only references within the schema are supported");
        var target = root;
        foreach (var segment in reference[2..].Split('/'))
            target = target.GetProperty(segment);
        return target;
    }

    private static Regex Pattern(string pattern)
    {
        lock (s_patterns)
        {
            if (!s_patterns.TryGetValue(pattern, out var regex))
                // Linear in the input, so a long string cannot stall a load, and no timeout: a 100 ms one failed a valid
                // package on a busy Android emulator.
                s_patterns[pattern] = regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
            return regex;
        }
    }

    private static JsonDocument Load(string resourceName)
    {
        using var stream = Open(resourceName);
        return JsonDocument.Parse(stream);
    }
}
