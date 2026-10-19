using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiSpineSampleApp.Pages.Avatars;

/// <summary>
/// An avatar the sample server hands out (GET /sample/avatars): the name, profile and version its manifest names, its file,
/// size and SHA-256. Its posters are at <c>sample/avatars/{Id}/poster-light.png</c> and <c>poster-dark.png</c>.
/// </summary>
public sealed record AvatarDownload(string Id, string Version, string File, long Bytes, string Sha256, string? Name = null, string? Profile = null)
{
    /// <summary>The size in megabytes, for a status line.</summary>
    public string Size => $"{Bytes / 1_000_000.0:0.0} MB";

    /// <summary>True when this is a later release than <paramref name="version"/> (a package's <c>assetVersion</c>, major.minor.patch).</summary>
    public bool IsNewerThan(string? version) =>
        version is null || !System.Version.TryParse(version, out var have) || !System.Version.TryParse(Version, out var offered) || offered > have;
}

[JsonSerializable(typeof(List<AvatarDownload>))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
internal sealed partial class AvatarDownloadJson : JsonSerializerContext;

/// <summary>
/// The avatars the app downloads rather than bundles: what the server offers, the version of the copy the app keeps, and the
/// download itself, checked against the server's hash before it replaces the copy. A source never touches the network; the
/// app opens the kept file with <c>AvatarSource.FromFile</c>.
/// </summary>
public static class AvatarDownloads
{
    /// <summary>The server's list, or null when it cannot be reached.</summary>
    public static async Task<IReadOnlyList<AvatarDownload>?> ListAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            return await http.GetFromJsonAsync("sample/avatars", AvatarDownloadJson.Default.ListAvatarDownload, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Where the app keeps downloaded avatars.</summary>
    public static string Folder { get; } = Path.Combine(FileSystem.AppDataDirectory, "avatars");

    /// <summary>Where the app keeps <paramref name="avatar"/>.</summary>
    public static string PathOf(AvatarDownload avatar) => Path.Combine(Folder, avatar.File);

    /// <summary>The avatars the app has kept, with the name each manifest gives: what a page can offer offline.</summary>
    public static IReadOnlyList<(string Path, string Name)> Kept()
    {
        if (!Directory.Exists(Folder))
            return [];
        return [.. Directory.EnumerateFiles(Folder, "*.spineavatar").Select(p => (p, NameOf(p) ?? Path.GetFileNameWithoutExtension(p))).OrderBy(k => k.Item2)];
    }

    /// <summary>The <c>displayName</c> of the package at <paramref name="path"/>, or null without one.</summary>
    public static string? NameOf(string path) => Read(path, "displayName");

    /// <summary>The <c>assetVersion</c> of the package at <paramref name="path"/>, or null without one (or without the file).</summary>
    public static string? VersionOf(string path) => Read(path, "assetVersion");

    private static string? Read(string path, string property)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            using var manifest = zip.GetEntry("avatar.json")?.Open();
            if (manifest is null)
                return null;
            using var json = JsonDocument.Parse(manifest);
            return json.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads <paramref name="avatar"/> to <paramref name="path"/>: written beside it first and moved over the kept copy only when
    /// complete and matching the server's SHA-256, so a cancelled or damaged download is never opened.
    /// </summary>
    public static async Task DownloadAsync(HttpClient http, AvatarDownload avatar, string path, IProgress<(long Read, long Total)>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".part";
        try
        {
            using var response = await http.GetAsync($"sample/avatars/{avatar.File}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? avatar.Bytes;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                    hash.AppendData(buffer, 0, n);
                    read += n;
                    progress?.Report((read, total));
                }
            }
            if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), avatar.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"{avatar.File} does not match the server's SHA-256; it was not kept.");
            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }
}
