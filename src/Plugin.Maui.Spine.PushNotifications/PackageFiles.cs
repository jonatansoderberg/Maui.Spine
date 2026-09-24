namespace Plugin.Maui.Spine.PushNotifications;

/// <summary>
/// Files that ship inside the app package (<c>MauiAsset</c>), copied out to the cache directory so
/// they have a path on the device. A notification's <see cref="LocalNotification.Image"/> needs
/// one; a file inside the package is not a path the system can read.
/// </summary>
public static class PackageFiles
{
    /// <summary>
    /// The cache path of <paramref name="fileName"/>, copying it out of the package the first time
    /// and again whenever the package's copy changed size (a new build).
    /// </summary>
    /// <param name="fileName">The package file name, e.g. <c>"map.png"</c>.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    public static async Task<string> CachedPathAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(FileSystem.CacheDirectory, "spine-package", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        await using var source = await FileSystem.OpenAppPackageFileAsync(fileName);

        if (File.Exists(target) && source.CanSeek && new FileInfo(target).Length == source.Length)
            return target;

        await using (var file = File.Create(target))
            await source.CopyToAsync(file, cancellationToken);

        return target;
    }
}
