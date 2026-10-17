using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MauiSpinePushNotificationsSampleApp.Server;

/// <summary>An avatar the server hands out: its id and asset version from the package's manifest, the file, its size and SHA-256.</summary>
public sealed record AvatarDownload(string Id, string Version, string File, long Bytes, string Sha256);

/// <summary>The <c>.spineavatar</c> files in a folder, read once at startup.</summary>
public static class AvatarCatalog
{
    public static IReadOnlyDictionary<string, AvatarDownload> Read(string folder)
    {
        var catalog = new SortedDictionary<string, AvatarDownload>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
            return catalog;
        foreach (var path in Directory.EnumerateFiles(folder, "*.spineavatar"))
        {
            using var stream = File.OpenRead(path);
            var sha = Convert.ToHexStringLower(SHA256.HashData(stream));
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            using var manifest = zip.GetEntry("avatar.json")?.Open() ?? throw new InvalidDataException($"{path} has no avatar.json.");
            using var json = JsonDocument.Parse(manifest);
            var id = json.RootElement.GetProperty("id").GetString()!;
            var version = json.RootElement.GetProperty("assetVersion").GetString()!;
            catalog[id] = new AvatarDownload(id, version, Path.GetFileName(path), new FileInfo(path).Length, sha);
        }
        return catalog;
    }
}
