using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Space on each side of a node, in points, as MAUI's <c>Thickness</c>. <see cref="Left"/> and <see cref="Right"/> are the
/// start and end of the line: a right-to-left language mirrors them. Written as <c>[left, top, right, bottom]</c>.
/// </summary>
[JsonConverter(typeof(WidgetThicknessJsonConverter))]
public readonly record struct WidgetThickness(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The same space on every side.</summary>
    public WidgetThickness(double uniform) : this(uniform, uniform, uniform, uniform) { }

    /// <summary><paramref name="horizontal"/> at the start and end, <paramref name="vertical"/> at the top and bottom.</summary>
    public WidgetThickness(double horizontal, double vertical) : this(horizontal, vertical, horizontal, vertical) { }

    /// <summary>Whether every side is the same.</summary>
    public bool IsUniform => Left == Top && Top == Right && Right == Bottom;

    internal double Smallest => Math.Min(Math.Min(Left, Top), Math.Min(Right, Bottom));

    internal void ThrowIfNegative()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Left);
        ArgumentOutOfRangeException.ThrowIfNegative(Top);
        ArgumentOutOfRangeException.ThrowIfNegative(Right);
        ArgumentOutOfRangeException.ThrowIfNegative(Bottom);
    }
}

internal sealed class WidgetThicknessJsonConverter : JsonConverter<WidgetThickness>
{
    public override WidgetThickness Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new WidgetThickness(reader.GetDouble());
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("A thickness is a number or [left, top, right, bottom].");
        var sides = new List<double>(4);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            sides.Add(reader.GetDouble());
        return sides.Count == 4
            ? new WidgetThickness(sides[0], sides[1], sides[2], sides[3])
            : throw new JsonException("A thickness is [left, top, right, bottom].");
    }

    public override void Write(Utf8JsonWriter writer, WidgetThickness value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Left);
        writer.WriteNumberValue(value.Top);
        writer.WriteNumberValue(value.Right);
        writer.WriteNumberValue(value.Bottom);
        writer.WriteEndArray();
    }
}
