namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Writes a file the renderer reads by renaming a finished copy over it. Refreshes run side by side — the
/// launch refresh, a background run, a button tap, a Live Activity — and write the same pictures and
/// documents. Written in place, the file is opened exclusively and the second writer fails, and a reader
/// can catch half a picture. Each writer gets its own temporary name, and the rename replaces the file whole.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var temp = Temporary(path);
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    public static async Task WriteAsync(string path, Stream contents, CancellationToken cancellationToken)
    {
        var temp = Temporary(path);
        try
        {
            await using (var file = File.Create(temp))
                await contents.CopyToAsync(file, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    private static string Temporary(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return $"{path}.{Guid.NewGuid():N}.tmp";
    }
}
