using SpineSymbols;

// spine-symbols <catalog.xcassets> <icon.svg>...: one <Name>.symbolset per SVG, named after the file.
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: spine-symbols <catalog.xcassets> <icon.svg>...");
    return 2;
}

var catalog = args[0];
Directory.CreateDirectory(catalog);
var catalogContents = Path.Combine(catalog, "Contents.json");
if (!File.Exists(catalogContents)) File.WriteAllText(catalogContents, Contents("""  "info" : { "author" : "xcode", "version" : 1 }"""));

var failed = false;
foreach (var file in args[1..])
{
    var name = Path.GetFileNameWithoutExtension(file);
    try
    {
        var result = SymbolOutline.Load(file);
        using var outline = result.Outline;
        if (outline.IsEmpty) throw new InvalidDataException("nothing is drawn");

        var set = Path.Combine(catalog, name + ".symbolset");
        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, name + ".svg"), SymbolTemplate.Write(outline, result.ViewBox));
        File.WriteAllText(Path.Combine(set, "Contents.json"), Contents($$"""
              "info" : { "author" : "xcode", "version" : 1 },
              "symbols" : [ { "filename" : "{{name}}.svg", "idiom" : "universal" } ]
            """));

        Console.WriteLine($"{name}: {set}");
        foreach (var warning in result.Warnings.Distinct()) Console.WriteLine($"{name}: warning: {warning}");
    }
    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"{name}: error: {e.Message}");
        failed = true;
    }
}
return failed ? 1 : 0;

static string Contents(string body) => "{\n" + body.TrimEnd() + "\n}\n";
