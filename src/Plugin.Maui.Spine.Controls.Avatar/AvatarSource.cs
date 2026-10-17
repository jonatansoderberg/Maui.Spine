namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Where a <c>.spineavatar</c> comes from: an asset bundled with the app, a file, or a stream the app opens. A source never
/// touches the network; an app downloads a large avatar itself, caches it and gives the file.
/// </summary>
public abstract class AvatarSource
{
    /// <summary>A name for logs and the screen reader's fallback.</summary>
    public abstract string Name { get; }

    /// <summary>Opens the package for reading; the view disposes the stream.</summary>
    public abstract Task<Stream> OpenAsync(CancellationToken cancellationToken);

    /// <summary>An asset bundled with the app (<c>MauiAsset</c>), by its logical path, such as <c>Avatars/nova.spineavatar</c>.</summary>
    public static AvatarSource FromMauiAsset(string path) => new MauiAssetSource(path);

    /// <summary>A file, such as a downloaded avatar in the app's data folder.</summary>
    public static AvatarSource FromFile(string path) => new FileSource(path);

    /// <summary>A stream the app opens itself.</summary>
    public static AvatarSource FromStream(string name, Func<CancellationToken, Task<Stream>> open) => new StreamSource(name, open);

    /// <inheritdoc />
    public override string ToString() => Name;

    private sealed class MauiAssetSource(string path) : AvatarSource
    {
        public override string Name => path;

        public override Task<Stream> OpenAsync(CancellationToken cancellationToken) => FileSystem.OpenAppPackageFileAsync(path);
    }

    private sealed class FileSource(string path) : AvatarSource
    {
        public override string Name => Path.GetFileName(path);

        public override Task<Stream> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<Stream>(File.OpenRead(path));
    }

    private sealed class StreamSource(string name, Func<CancellationToken, Task<Stream>> open) : AvatarSource
    {
        public override string Name => name;

        public override Task<Stream> OpenAsync(CancellationToken cancellationToken) => open(cancellationToken);
    }
}

/// <summary>Where an <see cref="AvatarView"/> is with its <see cref="AvatarView.Source"/>.</summary>
public enum AvatarLoadState
{
    /// <summary>No source.</summary>
    Empty,

    /// <summary>Reading and checking the package; the previous avatar, if any, stays on screen.</summary>
    Loading,

    /// <summary>Shown, by a renderer or, when the platform has none for it, as its poster.</summary>
    Loaded,

    /// <summary>The package did not load; <see cref="AvatarView.LoadError"/> says why.</summary>
    Failed,
}
