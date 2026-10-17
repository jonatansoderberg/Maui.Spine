using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// The signed-in user's picture, kept on disk for a day: from <see cref="SpineUser.PictureUrl"/> when the server has
/// one, otherwise from the provider (Microsoft's Graph). A picture that could not be fetched is not remembered, so the
/// next call tries again; the last one is kept meanwhile.
/// </summary>
internal sealed class PictureCache(HttpClient http, string directory, TimeProvider time)
{
    internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    private const string Prefix = "spine-picture-";

    /// <param name="user">Whose picture.</param>
    /// <param name="source">The provider's picture, when the user has no <see cref="SpineUser.PictureUrl"/>.</param>
    /// <param name="cancellationToken">Gives up.</param>
    /// <returns>The image's bytes (JPEG or PNG, as the provider has it), or null when the user has none.</returns>
    public async Task<byte[]?> GetAsync(SpineUser user, Func<CancellationToken, Task<byte[]?>>? source, CancellationToken cancellationToken)
    {
        var file = Path.Combine(directory, $"{Prefix}{Key(user)}.bin");
        if (File.Exists(file) && time.GetUtcNow() - File.GetLastWriteTimeUtc(file) < MaxAge) return Read(file);

        byte[]? picture;
        try
        {
            picture = user.PictureUrl is { } url ? await DownloadAsync(url, cancellationToken)
                : source is not null ? await source(cancellationToken)
                : null;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Offline, a provider that is down: the last picture is better than none, and nothing is remembered.
            return File.Exists(file) ? Read(file) : null;
        }

        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(file, picture ?? [], cancellationToken);
        return picture;
    }

    /// <summary>Forgets every user's picture: after a sign-out, no one's picture stays on the device.</summary>
    public void Clear()
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, $"{Prefix}*.bin")) File.Delete(file);
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    // An empty file is a user known to have no picture.
    private static byte[]? Read(string file) => File.ReadAllBytes(file) is { Length: > 0 } bytes ? bytes : null;

    // A new address is a new picture; the user's id stays off the file system.
    private static string Key(SpineUser user) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{user.Id}|{user.Provider}|{user.PictureUrl}")))[..32];
}
