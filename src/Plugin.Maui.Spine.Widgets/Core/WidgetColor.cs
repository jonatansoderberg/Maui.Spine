using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Widgets;

/// <summary>
/// A color in a widget tree: either one of the platform's semantic colors, which adapt to light
/// and dark appearance, or a fixed hex value.
/// </summary>
[JsonConverter(typeof(WidgetColorJsonConverter))]
public readonly record struct WidgetColor
{
    /// <summary>The serialized value: a semantic name or <c>#RRGGBB</c>.</summary>
    public string Value { get; }

    private WidgetColor(string value) => Value = value;

    /// <summary>The platform's primary text color.</summary>
    public static WidgetColor Primary { get; } = new("primary");
    /// <summary>The platform's secondary, de-emphasized text color.</summary>
    public static WidgetColor Secondary { get; } = new("secondary");
    /// <summary>The app's accent color.</summary>
    public static WidgetColor Accent { get; } = new("accent");
    /// <summary>The platform's semantic green.</summary>
    public static WidgetColor Green { get; } = new("green");
    /// <summary>The platform's semantic red.</summary>
    public static WidgetColor Red { get; } = new("red");
    /// <summary>The platform's semantic orange.</summary>
    public static WidgetColor Orange { get; } = new("orange");
    /// <summary>The platform's semantic yellow.</summary>
    public static WidgetColor Yellow { get; } = new("yellow");
    /// <summary>The platform's semantic blue.</summary>
    public static WidgetColor Blue { get; } = new("blue");

    /// <summary>A fixed color from a <c>#RRGGBB</c> or <c>#AARRGGBB</c> string.</summary>
    public static WidgetColor FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        var value = hex.StartsWith('#') ? hex : "#" + hex;
        if (value.Length is not (7 or 9) || !value[1..].All(Uri.IsHexDigit))
            throw new ArgumentException($"'{hex}' is not a #RRGGBB or #AARRGGBB color.", nameof(hex));
        return new WidgetColor(value.ToUpperInvariant());
    }

    /// <summary>A fixed color from a MAUI <see cref="Color"/>.</summary>
    public static WidgetColor From(Color color) => new(color.ToArgbHex());

    /// <inheritdoc />
    public override string ToString() => Value;
}

internal sealed class WidgetColorJsonConverter : JsonConverter<WidgetColor>
{
    public override WidgetColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Widget trees are only ever serialized by the app.");

    public override void Write(Utf8JsonWriter writer, WidgetColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
