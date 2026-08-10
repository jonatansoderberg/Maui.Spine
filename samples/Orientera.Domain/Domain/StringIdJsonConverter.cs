using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orientera.Domain;

/// <summary>Reads and writes an <see cref="IStringId{TSelf}"/> as a plain JSON string.</summary>
public sealed class StringIdJsonConverter<T> : JsonConverter<T>
    where T : IStringId<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        T.From(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);

    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        T.From(reader.GetString() ?? string.Empty);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value);
}
