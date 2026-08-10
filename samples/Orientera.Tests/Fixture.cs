using System.Xml.Linq;

namespace Orientera.Tests;

/// <summary>The recorded shape of an upstream response, loaded from <c>Fixtures/</c>.</summary>
internal static class Fixture
{
    public static XElement Eventor(string name) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Eventor", name)).Root!;
}
