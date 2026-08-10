using System.Text.Json;
using System.Xml.Linq;

namespace Orientera.Tests;

/// <summary>The shape of an upstream response, loaded from <c>Fixtures/</c>.</summary>
internal static class Fixture
{
    public static XElement Eventor(string name) =>
        XDocument.Load(PathFor("Eventor", name)).Root!;

    /// <summary>Recorded from the live service, tabs and all — see Fixtures/LiveResults/README.md.</summary>
    public static JsonElement LiveResults(string name) =>
        JsonSerializer.Deserialize<JsonElement>(
            Backend.LiveResults.LiveResultsClient.Repair(File.ReadAllText(PathFor("LiveResults", name))));

    public static string PathFor(string source, string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", source, name);
}
