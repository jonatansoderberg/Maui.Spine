namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>Where a <c>.spineavatar</c> comes from. Remote files are fetched by the app first; a source never touches the network.</summary>
public abstract class AvatarSource
{
    public abstract string Name { get; }

    public abstract Task<Stream> OpenAsync(CancellationToken cancellationToken);

    public static AvatarSource FromMauiAsset(string path) => new MauiAssetSource(path);

    public static AvatarSource FromFile(string path) => new FileSource(path);

    public static AvatarSource FromStream(string name, Func<CancellationToken, Task<Stream>> open) => new StreamSource(name, open);

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

public enum AvatarLoadState { Empty, Loading, Loaded, Failed }
