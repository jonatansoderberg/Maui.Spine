using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Plugin.Maui.Spine.Layouts;

/// <summary>One additional file: a layout, a resources file or a strings document.</summary>
internal sealed class SourceFile(string path, SourceText text)
{
    public string Path { get; } = path;
    public SourceText Text { get; } = text;

    public static bool IsLayout(string path) => path.EndsWith(".layout.xml", StringComparison.OrdinalIgnoreCase);

    public static bool IsResources(string path) => path.EndsWith(".resources.xml", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>strings.xml</c> is the neutral document, <c>strings.sv.xml</c> Swedish, as <c>EmbeddedXmlStringProvider</c> reads them.</summary>
    public static string? StringsCulture(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var match = Regex.Match(name, @"^strings(?:\.([A-Za-z]{2,3}(?:-[A-Za-z0-9]+)*))?\.xml$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    public Location Location(XObject? node, int length = 0)
    {
        if (node is not IXmlLineInfo info || !info.HasLineInfo() || info.LineNumber - 1 >= Text.Lines.Count)
            return Microsoft.CodeAnalysis.Location.Create(Path, new TextSpan(0, 0), new LinePositionSpan(default, default));
        var line = Text.Lines[info.LineNumber - 1];
        var start = Math.Min(line.Start + Math.Max(0, info.LinePosition - 1), line.End);
        var end = Math.Min(start + length, line.End);
        var position = new LinePosition(info.LineNumber - 1, start - line.Start);
        return Microsoft.CodeAnalysis.Location.Create(Path, TextSpan.FromBounds(start, end),
            new LinePositionSpan(position, new LinePosition(info.LineNumber - 1, end - line.Start)));
    }

    public XDocument? Load(List<Diagnostic> diagnostics)
    {
        try
        {
            var document = XDocument.Parse(Text.ToString(), LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            document.AddAnnotation(this);
            return document;
        }
        catch (XmlException e)
        {
            var line = Math.Max(0, e.LineNumber - 1);
            var position = new LinePosition(line, Math.Max(0, e.LinePosition - 1));
            var span = line < Text.Lines.Count ? new TextSpan(Text.Lines[line].Start, 0) : new TextSpan(0, 0);
            diagnostics.Add(Diagnostic.Create(Diagnostics.BadXml,
                Microsoft.CodeAnalysis.Location.Create(Path, span, new LinePositionSpan(position, position)), e.Message));
            return null;
        }
    }
}

/// <summary>The app's strings by culture ("" neutral), for checking <c>{Loc …}</c>.</summary>
internal sealed class StringsTable
{
    public Dictionary<string, Dictionary<string, string>> Cultures { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static StringsTable From(IEnumerable<SourceFile> files)
    {
        var table = new StringsTable();
        foreach (var file in files)
        {
            if (SourceFile.StringsCulture(file.Path) is not { } culture)
                continue;
            XDocument document;
            try { document = XDocument.Parse(file.Text.ToString()); }
            catch (XmlException) { continue; }
            if (document.Root is null)
                continue;
            if (!table.Cultures.TryGetValue(culture, out var strings))
                table.Cultures[culture] = strings = new Dictionary<string, string>(StringComparer.Ordinal);
            // Spine's form, <s key="…">, and the Android-like <string name="…">.
            foreach (var element in document.Root.Elements())
                if ((element.Attribute("key") ?? element.Attribute("name"))?.Value is { Length: > 0 } key)
                    strings[key] = element.Value;
        }
        return table;
    }

    public static int Arguments(string text) =>
        Regex.Matches(text, @"\{(\d+)[^}]*\}").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value) + 1).DefaultIfEmpty(0).Max();
}
