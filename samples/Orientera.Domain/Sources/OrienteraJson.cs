using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orientera.Services.Sources;

/// <summary>
/// The one JSON shape the domain travels in — over the BFF contract and into the offline
/// packages. Both ends read it from here so a change can never apply to only one of them.
/// </summary>
public static class OrienteraJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Enums by name: a stored package or a logged response has to stay readable, and
        // renumbering an enum must not silently turn a sprint into a night race.
        Converters = { new TolerantEnumConverter() },
    };
}

/// <summary>
/// Enums by name, where a name this version does not know reads as the default instead of
/// throwing.
/// </summary>
/// <remarks>
/// Packages and cached responses outlive the app version that wrote them. When <c>Indoor</c>
/// moved off <c>Discipline</c> and onto <c>Sport</c>, every stored package that mentioned it
/// became unreadable — and because the calendar is deserialised in one piece, a single unknown
/// word emptied the whole list. One value we cannot name is one field read wrong; throwing is
/// the entire screen.
/// <para>
/// Writing is unchanged: this app always writes names it knows.
/// </para>
/// </remarks>
public sealed class TolerantEnumConverter : JsonConverterFactory
{
    private static readonly JsonStringEnumConverter Names = new();

    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var inner = Names.CreateConverter(typeToConvert, options);

        return (JsonConverter)Activator.CreateInstance(
            typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert), inner)!;
    }
}

internal sealed class TolerantEnumConverter<T>(JsonConverter inner) : JsonConverter<T>
    where T : struct, Enum
{
    private readonly JsonConverter<T> _inner = (JsonConverter<T>)inner;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // The reader has to be restored on failure: the inner converter may have consumed the
        // token before deciding it did not know the word.
        var snapshot = reader;

        try
        {
            return _inner.Read(ref reader, typeToConvert, options);
        }
        catch (JsonException)
        {
            reader = snapshot;
            reader.Skip();
            return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        _inner.Write(writer, value, options);
}
