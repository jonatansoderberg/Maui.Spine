using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Images;

/// <summary>An image's bytes: a file in the cache, or the bytes themselves when caching is off.</summary>
internal readonly record struct ImageFile(string? Path, byte[]? Bytes)
{
    public Stream OpenRead() => Path is not null
        ? File.OpenRead(Path)
        : new MemoryStream(Bytes ?? [], writable: false);
}

/// <summary>
/// Remote images on disk: one file per URL, named by its SHA-256. A file counts as fresh for the
/// source's <c>CacheValidity</c> after it was written; a stale file is downloaded again, and kept and used
/// when the download fails, so images still show offline. Concurrent requests for one URL share one
/// download; at most <see cref="SpineImagesOptions.MaxConcurrentDownloads"/> run at a time. The directory
/// is trimmed to three quarters of <see cref="SpineImagesOptions.DiskCacheSize"/>, least recently used first,
/// when it grows past it.
/// </summary>
internal sealed class ImageFileCache
{
    const string Extension = ".img";

    readonly string _directory;
    readonly long _maxBytes;
    readonly HttpClient _http;
    readonly TimeProvider _time;
    readonly ILogger? _logger;
    readonly SemaphoreSlim _downloads;
    readonly ConcurrentDictionary<string, Task<ImageFile>> _inFlight = new();
    readonly Lock _trimLock = new();
    long _bytes;
    int _trimming;
    int _downloadCount;

    public ImageFileCache(string directory, SpineImagesOptions options, HttpClient http, TimeProvider? time = null, ILogger? logger = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrentDownloads, 1);
        _directory = directory;
        _maxBytes = options.DiskCacheSize;
        _http = http;
        _time = time ?? TimeProvider.System;
        _logger = logger;
        _downloads = new SemaphoreSlim(options.MaxConcurrentDownloads);

        // Measures what an earlier run left, and trims it if the limit has shrunk since.
        ScheduleTrim();
    }

    public string Directory => _directory;

    /// <summary>The number of requests that went to the network since start.</summary>
    public int DownloadCount => Volatile.Read(ref _downloadCount);

    public string PathFor(Uri uri) => System.IO.Path.Combine(_directory, Key(uri) + Extension);

    public bool Contains(Uri uri) => File.Exists(PathFor(uri));

    /// <summary>
    /// The image at <paramref name="uri"/>, from the disk when it is there and fresh, else from the network.
    /// </summary>
    /// <param name="uri">An absolute http or https URL.</param>
    /// <param name="validity">How long a file counts as fresh after it was downloaded.</param>
    /// <param name="cachingEnabled">False skips the disk both ways, as MAUI's <c>CachingEnabled</c>.</param>
    /// <param name="cancellationToken">Stops waiting; a download other callers share runs on.</param>
    public Task<ImageFile> GetAsync(Uri uri, TimeSpan validity, bool cachingEnabled = true, CancellationToken cancellationToken = default)
    {
        if (!cachingEnabled)
            return DownloadBytesAsync(uri, cancellationToken);

        var path = PathFor(uri);
        var info = new FileInfo(path);
        if (info.Exists && info.LastWriteTimeUtc + validity > _time.GetUtcNow().UtcDateTime)
        {
            Touch(path);
            return Task.FromResult(new ImageFile(path, null));
        }

        var stale = info.Exists;
        var shared = _inFlight.GetOrAdd(path, _ => DownloadToFileAsync(uri, path, stale));
        return shared.IsCompleted ? shared : shared.WaitAsync(cancellationToken);
    }

    /// <summary>Downloads every URL that is not on disk and fresh; failures are thrown together at the end.</summary>
    public async Task PrefetchAsync(IEnumerable<Uri> uris, TimeSpan validity, CancellationToken cancellationToken = default)
    {
        var tasks = uris.Distinct().Select(uri => GetAsync(uri, validity, cancellationToken: cancellationToken)).ToArray();
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch when (tasks.Count(static t => t.IsFaulted) > 1)
        {
            throw new AggregateException(
                $"{tasks.Count(static t => t.IsFaulted)} of {tasks.Length} images could not be prefetched.",
                tasks.Where(static t => t.IsFaulted).SelectMany(static t => t.Exception!.InnerExceptions));
        }
    }

    public Task ClearAsync() => Task.Run(() =>
    {
        lock (_trimLock)
        {
            if (System.IO.Directory.Exists(_directory))
                foreach (var file in System.IO.Directory.EnumerateFiles(_directory))
                    TryDelete(file);
            Interlocked.Exchange(ref _bytes, 0);
        }
    });

    /// <summary>Deletes the least recently used files until the directory is at three quarters of the limit.</summary>
    public void Trim()
    {
        lock (_trimLock)
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                Interlocked.Exchange(ref _bytes, 0);
                return;
            }

            var directory = new DirectoryInfo(_directory);

            // A download the app was closed in the middle of leaves its temporary file behind.
            var abandoned = _time.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(10);
            foreach (var temporary in directory.GetFiles("*.tmp"))
                if (temporary.LastWriteTimeUtc < abandoned)
                    TryDelete(temporary.FullName);

            var files = directory.GetFiles("*" + Extension);
            var total = files.Sum(static f => f.Length);
            if (total > _maxBytes)
            {
                var target = _maxBytes * 3 / 4;
                foreach (var file in files.OrderBy(static f => f.LastAccessTimeUtc))
                {
                    if (total <= target)
                        break;
                    if (TryDelete(file.FullName))
                        total -= file.Length;
                }
            }

            Interlocked.Exchange(ref _bytes, total);
        }
    }

    async Task<ImageFile> DownloadToFileAsync(Uri uri, string path, bool stale)
    {
        // Leave the caller's synchronous path before the semaphore, so GetOrAdd has stored this task
        // before it can complete and remove itself.
        await Task.Yield();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            long length;
            // Streamed to the file through a pooled buffer: a list of photos would otherwise allocate a
            // large array per image, and the collections that follow stop the main thread too.
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous))
                length = await DownloadAsync(uri, file, CancellationToken.None).ConfigureAwait(false);

            File.SetLastWriteTimeUtc(temporary, _time.GetUtcNow().UtcDateTime);
            File.Move(temporary, path, overwrite: true);
            Touch(path);

            if (Interlocked.Add(ref _bytes, length) > _maxBytes)
                ScheduleTrim();

            return new ImageFile(path, null);
        }
        catch (Exception exception) when (stale && File.Exists(path))
        {
            _logger?.LogWarning(exception, "Showing the cached copy of {Uri}: it is past its CacheValidity and could not be downloaded again.", uri);
            Touch(path);
            return new ImageFile(path, null);
        }
        finally
        {
            TryDelete(temporary);
            _inFlight.TryRemove(path, out _);
        }
    }

    async Task<ImageFile> DownloadBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await DownloadAsync(uri, memory, cancellationToken).ConfigureAwait(false);
        return new(null, memory.ToArray());
    }

    async Task<long> DownloadAsync(Uri uri, Stream destination, CancellationToken cancellationToken)
    {
        await _downloads.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Interlocked.Increment(ref _downloadCount);
            var started = _time.GetTimestamp();
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} for {uri}", null, response.StatusCode);

            await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await body.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

            var length = destination.Position;
            if (length == 0)
                throw new HttpRequestException($"{uri} answered {(int)response.StatusCode} with no body.");

            _logger?.LogDebug("Downloaded {Uri}: {Bytes} bytes in {Milliseconds:F0} ms.", uri, length, _time.GetElapsedTime(started).TotalMilliseconds);
            return length;
        }
        finally
        {
            _downloads.Release();
        }
    }

    void ScheduleTrim()
    {
        if (Interlocked.Exchange(ref _trimming, 1) == 1)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                Trim();
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "Could not trim the image cache in {Directory}.", _directory);
            }
            finally
            {
                Volatile.Write(ref _trimming, 0);
            }
        });
    }

    void Touch(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, _time.GetUtcNow().UtcDateTime);
        }
        catch (IOException)
        {
            // Trimmed or replaced meanwhile; the next read finds out.
        }
    }

    static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string Key(Uri uri) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
}
