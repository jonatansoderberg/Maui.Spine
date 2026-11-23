using System.Xml.Linq;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// What the build declared to the widget board, read back from the installed package's manifest: the class the board
/// creates the provider from, and one definition per <c>&lt;SpineWidget&gt;</c>, whose id is the kind. An unpackaged
/// app has no manifest, and the board does not know it.
/// </summary>
internal sealed record WidgetBoardManifest(Guid ClassId, IReadOnlyList<string> Kinds)
{
    public const string ExtensionName = "com.microsoft.windows.widgets";

    public static WidgetBoardManifest? Read()
    {
        if (AppInfo.Current.PackagingModel != AppPackagingModel.Packaged) return null;

        string path;
        try { path = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "AppxManifest.xml"); }
        catch (InvalidOperationException) { return null; }
        if (!File.Exists(path)) return null;

        var manifest = XDocument.Load(path);
        var extension = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "AppExtension" && (string?)e.Attribute("Name") == ExtensionName);
        if (extension?.Descendants().FirstOrDefault(e => e.Name.LocalName == "CreateInstance")?.Attribute("ClassId")?.Value is not { } id
            || !Guid.TryParse(id, out var classId))
            return null;

        var kinds = extension.Descendants().Where(e => e.Name.LocalName == "Definition")
            .Select(e => (string?)e.Attribute("Id")).OfType<string>().ToList();
        return new WidgetBoardManifest(classId, kinds);
    }
}
