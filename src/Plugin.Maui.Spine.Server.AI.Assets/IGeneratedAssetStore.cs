using System.Collections.Concurrent;

namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>An archived image.</summary>
/// <param name="Url">Where the app fetches it, without the cache-buster.</param>
/// <param name="LastModified">When it was written; the cache-buster.</param>
public sealed record StoredAsset(Uri Url, DateTimeOffset LastModified);

/// <summary>Why the last attempt at an image failed, kept where the app cannot read it.</summary>
/// <param name="Reason">The kind of failure.</param>
/// <param name="FailedAt">When.</param>
/// <param name="Attempts">How many attempts were made.</param>
/// <param name="RetryAfter">When a lookup may order it again.</param>
public sealed record AssetFailureRecord(GeneratedAssetFailure Reason, DateTimeOffset FailedAt, int Attempts, DateTimeOffset RetryAfter);

/// <summary>
/// Where generated images are archived, and where failures are noted. Paths are
/// <c>{feature}/v{version}/{key}</c>, with the extension on images.
/// </summary>
public interface IGeneratedAssetStore
{
    /// <summary>Creates what the store needs; called once when the host starts.</summary>
    Task ProvisionAsync(CancellationToken cancellationToken);

    /// <summary>The image at <paramref name="path"/>, or <see langword="null"/>.</summary>
    Task<StoredAsset?> GetAsync(string path, CancellationToken cancellationToken);

    /// <summary>Archives an image, replacing one at the same path.</summary>
    Task<StoredAsset> PutAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    /// <summary>The failure noted for <paramref name="path"/>, or <see langword="null"/>.</summary>
    Task<AssetFailureRecord?> GetFailureAsync(string path, CancellationToken cancellationToken);

    /// <summary>Notes a failure, replacing an earlier one.</summary>
    Task SetFailureAsync(string path, AssetFailureRecord failure, CancellationToken cancellationToken);

    /// <summary>Removes the failure noted for <paramref name="path"/>, if any.</summary>
    Task DeleteFailureAsync(string path, CancellationToken cancellationToken);
}

/// <summary>Keeps images in the process. For tests and samples.</summary>
/// <param name="baseUrl">The URLs the images get; <c>https://assets.invalid/</c> by default.</param>
/// <param name="time">The clock for <see cref="StoredAsset.LastModified"/>.</param>
public sealed class InMemoryGeneratedAssetStore(Uri? baseUrl = null, TimeProvider? time = null) : IGeneratedAssetStore
{
    private readonly Uri _base = baseUrl ?? new Uri("https://assets.invalid/");
    private readonly ConcurrentDictionary<string, (byte[] Content, string ContentType, StoredAsset Asset)> _images = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AssetFailureRecord> _failures = new(StringComparer.Ordinal);

    /// <summary>The archived paths.</summary>
    public IEnumerable<string> Paths => _images.Keys;

    /// <summary>The bytes and type archived at <paramref name="path"/>.</summary>
    public (byte[] Content, string ContentType)? Read(string path) =>
        _images.TryGetValue(path, out var image) ? (image.Content, image.ContentType) : null;

    /// <inheritdoc />
    public Task ProvisionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<StoredAsset?> GetAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(_images.TryGetValue(path, out var image) ? image.Asset : null);

    /// <inheritdoc />
    public Task<StoredAsset> PutAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        var asset = new StoredAsset(new Uri(_base, path), (time ?? TimeProvider.System).GetUtcNow());
        _images[path] = (content.ToArray(), contentType, asset);
        return Task.FromResult(asset);
    }

    /// <inheritdoc />
    public Task<AssetFailureRecord?> GetFailureAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(_failures.TryGetValue(path, out var failure) ? failure : null);

    /// <inheritdoc />
    public Task SetFailureAsync(string path, AssetFailureRecord failure, CancellationToken cancellationToken)
    {
        _failures[path] = failure;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteFailureAsync(string path, CancellationToken cancellationToken)
    {
        _failures.TryRemove(path, out _);
        return Task.CompletedTask;
    }
}
