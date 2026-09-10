using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common;

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

    /// <summary>
    /// The platform's widget surface: the system background on iOS, and Material You's on Android 12 and
    /// later — what a box takes to sit on the widget as a card.
    /// </summary>
    public static WidgetColor Surface { get; } = new("surface");

    /// <summary>Text and icons on an <see cref="Accent"/> fill: white on iOS, and Material You's on Android 12 and later.</summary>
    public static WidgetColor OnAccent { get; } = new("onAccent");
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

    /// <summary>
    /// A fixed color from a <see cref="System.Drawing.Color"/>. The BCL type, so a server that
    /// builds trees without MAUI has a color input other than <see cref="FromHex"/>. There is no
    /// conversion between this and MAUI's <c>Color</c>; the app-side overload lives in
    /// <c>Plugin.Maui.Spine.Widgets</c>.
    /// </summary>
    public static WidgetColor From(System.Drawing.Color color) =>
        FromHex($"#{color.R:X2}{color.G:X2}{color.B:X2}");

    /// <summary>Reads back what <see cref="Value"/> wrote: a semantic name, or a hex color.</summary>
    /// <param name="value">The serialized form.</param>
    /// <returns>The color.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is neither a known name nor a hex color.</exception>
    public static WidgetColor Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value switch
        {
            "primary" => Primary,
            "secondary" => Secondary,
            "accent" => Accent,
            "surface" => Surface,
            "onAccent" => OnAccent,
            "green" => Green,
            "red" => Red,
            "orange" => Orange,
            "yellow" => Yellow,
            "blue" => Blue,
            _ => FromHex(value),
        };
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

internal sealed class WidgetColorJsonConverter : JsonConverter<WidgetColor>
{
    // Trees are read back as well as written since Spine.Push: on Android a Live Update arrives as
    // serialized layout in a data message and is rendered in the app's process.
    public override WidgetColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { Length: > 0 } value ? WidgetColor.Parse(value) : default;

    public override void Write(Utf8JsonWriter writer, WidgetColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
