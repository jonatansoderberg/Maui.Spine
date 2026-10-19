using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiSpinePushNotificationsSampleApp.Server;

/// <summary>
/// An avatar the server hands out: its id, name, profile and asset version from the package's manifest, the file, its size
/// and SHA-256, so an app can list it before it downloads it.
/// </summary>
public sealed record AvatarDownload(string Id, string Name, string Profile, string Version, string File, long Bytes, string Sha256)
{
    /// <summary>The package's light and dark posters, served beside the list (<c>/sample/avatars/{id}/poster-light.png</c>).</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, byte[]> Posters { get; init; } = new Dictionary<string, byte[]>();
}

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
            using var json = JsonDocument.Parse(Bytes(zip, "avatar.json") ?? throw new InvalidDataException($"{path} has no avatar.json."));
            var root = json.RootElement;
            var id = root.GetProperty("id").GetString()!;
            var posters = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (root.TryGetProperty("posters", out var named))
            {
                foreach (var theme in new[] { "light", "dark" })
                {
                    if (named.TryGetProperty(theme, out var entry) && Bytes(zip, entry.GetString()!) is { } png)
                        posters[theme] = png;
                }
            }
            catalog[id] = new AvatarDownload(
                id,
                root.TryGetProperty("displayName", out var name) ? name.GetString()! : id,
                root.TryGetProperty("profile", out var profile) ? profile.GetString()! : "character",
                root.GetProperty("assetVersion").GetString()!,
                Path.GetFileName(path), new FileInfo(path).Length, sha)
            { Posters = posters };
        }
        return catalog;
    }

    private static byte[]? Bytes(ZipArchive zip, string path)
    {
        if (zip.GetEntry(path) is not { } entry)
            return null;
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
