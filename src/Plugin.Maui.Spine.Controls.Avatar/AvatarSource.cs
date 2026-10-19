using System.ComponentModel;
using System.Globalization;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Where a <c>.spineavatar</c> comes from: one built into Spine, an asset bundled with the app, a file, or a stream the app
/// opens. A source never touches the network; an app downloads a large avatar itself, caches it and gives the file.
/// </summary>
/// <remarks>
/// In XAML a string is enough: the name of a built-in avatar (<c>Source="bars"</c>), or an asset's path
/// (<c>Source="Avatars/nova.spineavatar"</c>).
/// </remarks>
[TypeConverter(typeof(AvatarSourceConverter))]
public abstract class AvatarSource
{
    /// <summary>
    /// The avatars built into Spine: <c>bars</c> (the answer's 24 bands up from a line, the microphone's down) and
    /// <c>ring</c> (the answer's bands outward and the microphone's inward round an empty middle). Both show a voice's sound
    /// in and out: the answer in their <c>output</c> slot, the microphone in their <c>accent</c> slot, which follows the app's
    /// accent.
    /// </summary>
    public static IReadOnlyList<string> BuiltInNames { get; } = ["bars", "ring"];

    /// <summary>An avatar built into Spine, by one of <see cref="BuiltInNames"/>.</summary>
    /// <exception cref="ArgumentException">No built-in avatar has that name.</exception>
    public static AvatarSource BuiltIn(string name)
    {
        var known = BuiltInNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"'{name}' is not a built-in avatar; there are {string.Join(", ", BuiltInNames)}.", nameof(name));
        return new BuiltInSource(known);
    }

    /// <summary>A built-in avatar's name or an asset's path, as XAML gives it.</summary>
    public static implicit operator AvatarSource(string nameOrPath) => Parse(nameOrPath);

    internal static AvatarSource Parse(string nameOrPath) =>
        BuiltInNames.Contains(nameOrPath, StringComparer.OrdinalIgnoreCase) ? BuiltIn(nameOrPath) : FromMauiAsset(nameOrPath);

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

    private sealed class BuiltInSource(string name) : AvatarSource
    {
        public override string Name => name;

        public override Task<Stream> OpenAsync(CancellationToken cancellationToken) =>
            Task.FromResult(typeof(AvatarSource).Assembly.GetManifestResourceStream($"Plugin.Maui.Spine.Controls.Avatar.{name}.spineavatar")
                ?? throw new InvalidOperationException($"{name}.spineavatar is missing from Plugin.Maui.Spine.Controls.Avatar."));
    }

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

/// <summary>Turns XAML's string into an <see cref="AvatarSource"/>: a built-in avatar's name, or an asset's path.</summary>
public sealed class AvatarSourceConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string { Length: > 0 } text ? AvatarSource.Parse(text.Trim()) : null;
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
