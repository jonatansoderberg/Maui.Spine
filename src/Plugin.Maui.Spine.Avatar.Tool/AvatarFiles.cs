using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Controls.Avatar;

namespace Plugin.Maui.Spine.Avatar.Tool;

/// <summary>Reads a package with its report, and writes one with a fresh hash table.</summary>
internal static class AvatarFiles
{
    public const string Source = "source/";

    public static (AvatarPackage? Package, AvatarValidationReport Report) Read(string path)
    {
        var report = new AvatarValidationReport();
        using var stream = File.OpenRead(path);
        return (AvatarArchive.TryRead(stream, report), report);
    }

    /// <summary>The files other than the manifest, as they are.</summary>
    public static SortedDictionary<string, byte[]> Contents(AvatarPackage package) =>
        new(package.FilePaths.Where(p => p != AvatarArchive.ManifestPath).ToDictionary(p => p, p => package.GetFile(p).ToArray()), StringComparer.Ordinal);

    /// <summary>
    /// Writes <paramref name="manifest"/> with its <c>files</c> table made from <paramref name="files"/> (path, SHA-256, size),
    /// then the files, to <paramref name="path"/>.
    /// </summary>
    public static void Write(string path, JsonObject manifest, SortedDictionary<string, byte[]> files)
    {
        manifest["files"] = new JsonArray([.. files.Select(f => (JsonNode)new JsonObject
        {
            ["path"] = f.Key,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(f.Value)),
            ["bytes"] = f.Value.Length,
        })]);

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } directory)
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            Add(zip, AvatarArchive.ManifestPath, Encoding.UTF8.GetBytes(manifest.ToJsonString(new() { WriteIndented = true })));
            foreach (var (name, bytes) in files)
                Add(zip, name, bytes);
        }
        File.Move(temporary, path, overwrite: true);
    }

    public static JsonObject Manifest(AvatarPackage package) =>
        JsonNode.Parse(package.GetFile(AvatarArchive.ManifestPath).Span)!.AsObject();

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        // PNGs are compressed already.
        var level = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
        using var entry = zip.CreateEntry(name, level).Open();
        entry.Write(bytes);
    }
}
