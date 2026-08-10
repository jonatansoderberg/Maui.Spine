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
        Converters = { new JsonStringEnumConverter() },
    };
}
