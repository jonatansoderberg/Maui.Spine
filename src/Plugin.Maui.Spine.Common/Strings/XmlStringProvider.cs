using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Strings from XML documents of the form
/// <c>&lt;strings culture="sv"&gt;&lt;s key="Home.Greeting"&gt;Hej {0}!&lt;/s&gt;&lt;/strings&gt;</c>.
/// One document per culture; the neutral document has no <c>culture</c> attribute, or an empty one.
/// </summary>
public class XmlStringProvider : IStringProvider
{
    private readonly Dictionary<string, Func<Stream>> _documents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a document for <paramref name="culture"/> (empty for the neutral one).</summary>
    public XmlStringProvider Add(string culture, Func<Stream> open)
    {
        _documents[culture] = open;
        return this;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string>? Load(CultureInfo culture)
    {
        if (!_documents.TryGetValue(culture.Name, out var open))
            return null;

        using var stream = open();
        return Parse(stream);
    }

    /// <summary>Parses one document into its key/value pairs.</summary>
    public static IReadOnlyDictionary<string, string> Parse(Stream stream)
    {
        var document = XDocument.Load(stream);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var element in document.Root?.Elements("s") ?? [])
        {
            if (element.Attribute("key")?.Value is { Length: > 0 } key)
                strings[key] = element.Value;
        }

        return strings;
    }
}

/// <summary>
/// Strings from XML documents embedded in an assembly: a manifest resource named
/// <c>strings.xml</c> is the neutral document and <c>strings.&lt;culture&gt;.xml</c> is that
/// culture's, wherever they sit in the assembly's folder structure.
/// </summary>
public sealed class EmbeddedXmlStringProvider : XmlStringProvider
{
    /// <summary>Finds the documents in <paramref name="assembly"/>.</summary>
    public EmbeddedXmlStringProvider(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (Culture(name) is { } culture)
                Add(culture, () => assembly.GetManifestResourceStream(name)!);
        }
    }

    /// <summary>The culture a resource name stands for, or <see langword="null"/> when it is not a strings document.</summary>
    internal static string? Culture(string resourceName)
    {
        // "App.Resources.Strings.strings.sv-SE.xml" → "sv-SE"; "App.Resources.strings.xml" → "".
        if (!resourceName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return null;

        var stem = resourceName[..^4];
        var marker = stem.LastIndexOf("strings", StringComparison.OrdinalIgnoreCase);

        if (marker < 0 || (marker > 0 && stem[marker - 1] != '.'))
            return null;

        var rest = stem[(marker + "strings".Length)..];

        if (rest.Length == 0)
            return "";

        return rest[0] == '.' && rest.Length > 1 && !rest[1..].Contains('.') ? rest[1..] : null;
    }
}
