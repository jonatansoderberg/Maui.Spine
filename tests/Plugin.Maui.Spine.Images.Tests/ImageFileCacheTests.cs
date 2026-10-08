using System.Net;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Plugin.Maui.Spine.Images.Tests;

public sealed class ImageFileCacheTests : IDisposable
{
    static readonly Uri Photo = new("https://example.org/photo.jpg");

    readonly string _directory = Path.Combine(Path.GetTempPath(), "spine-images-tests", Guid.NewGuid().ToString("N"));
    readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    readonly FakeServer _server = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    ImageFileCache Cache(long diskCacheSize = 1024 * 1024, int maxConcurrentDownloads = 4) =>
        new(_directory, new SpineImagesOptions { DiskCacheSize = diskCacheSize, MaxConcurrentDownloads = maxConcurrentDownloads }, new HttpClient(_server), _time);

    [Fact]
    public async Task A_second_load_comes_from_disk()
    {
        var cache = Cache();

        var first = await cache.GetAsync(Photo, TimeSpan.FromDays(1));
        var second = await cache.GetAsync(Photo, TimeSpan.FromDays(1));

        Assert.Equal(1, _server.Requests);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal(FakeServer.Body(Photo), await File.ReadAllBytesAsync(second.Path!));
        Assert.True(cache.Contains(Photo));
    }

    [Fact]
    public async Task Concurrent_loads_share_one_download()
    {
        var cache = Cache();
        _server.Gate = new TaskCompletionSource();

        var loads = Enumerable.Range(0, 5).Select(_ => cache.GetAsync(Photo, TimeSpan.FromDays(1))).ToArray();
        _server.Gate.SetResult();
        await Task.WhenAll(loads);

        Assert.Equal(1, _server.Requests);
    }

    [Fact]
    public async Task A_stale_file_is_downloaded_again()
    {
        var cache = Cache();
        await cache.GetAsync(Photo, TimeSpan.FromHours(1));

        _time.Advance(TimeSpan.FromHours(2));
        await cache.GetAsync(Photo, TimeSpan.FromHours(1));

        Assert.Equal(2, _server.Requests);
    }

    [Fact]
    public async Task A_stale_file_is_shown_when_the_network_fails()
    {
        var cache = Cache();
        var first = await cache.GetAsync(Photo, TimeSpan.FromHours(1));

        _time.Advance(TimeSpan.FromHours(2));
        _server.Status = HttpStatusCode.ServiceUnavailable;
        var second = await cache.GetAsync(Photo, TimeSpan.FromHours(1));

        Assert.Equal(first.Path, second.Path);
        Assert.Equal(2, _server.Requests);
    }

    [Fact]
    public async Task A_failed_download_names_the_status_and_the_url()
    {
        var cache = Cache();
        _server.Status = HttpStatusCode.NotFound;

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync(Photo, TimeSpan.FromDays(1)));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("404", exception.Message);
        Assert.Contains(Photo.AbsoluteUri, exception.Message);
        Assert.False(cache.Contains(Photo));
    }

    [Fact]
    public async Task Caching_off_keeps_nothing_on_disk()
    {
        var cache = Cache();

        var file = await cache.GetAsync(Photo, TimeSpan.FromDays(1), cachingEnabled: false);
        await cache.GetAsync(Photo, TimeSpan.FromDays(1), cachingEnabled: false);

        Assert.Null(file.Path);
        Assert.Equal(FakeServer.Body(Photo), file.Bytes);
        Assert.Equal(2, _server.Requests);
        Assert.False(cache.Contains(Photo));
    }

    [Fact]
    public async Task Prefetch_downloads_each_url_once()
    {
        var cache = Cache();
        var uris = Enumerable.Range(0, 6).Select(i => new Uri($"https://example.org/{i}.jpg")).ToList();

        await cache.PrefetchAsync([.. uris, .. uris], TimeSpan.FromDays(1));
        await cache.PrefetchAsync(uris, TimeSpan.FromDays(1));

        Assert.Equal(6, _server.Requests);
        Assert.All(uris, uri => Assert.True(cache.Contains(uri)));
    }

    [Fact]
    public async Task Prefetch_reports_every_failure()
    {
        var cache = Cache();
        _server.Status = HttpStatusCode.Forbidden;

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            cache.PrefetchAsync([new("https://example.org/a.jpg"), new("https://example.org/b.jpg")], TimeSpan.FromDays(1)));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains("2 of 2", exception.Message);
    }

    [Fact]
    public async Task Downloads_stay_within_the_limit()
    {
        var cache = Cache(maxConcurrentDownloads: 2);
        _server.Gate = new TaskCompletionSource();

        var loads = Enumerable.Range(0, 6).Select(i => cache.GetAsync(new Uri($"https://example.org/{i}.jpg"), TimeSpan.FromDays(1))).ToArray();
        await Task.Delay(100);
        Assert.Equal(2, _server.Requests);

        _server.Gate.SetResult();
        await Task.WhenAll(loads);
        Assert.Equal(6, _server.Requests);
        Assert.Equal(2, _server.MaxConcurrent);
    }

    [Fact]
    public async Task Trim_deletes_the_least_recently_used_first()
    {
        // Each body is 1 000 bytes; the limit holds three, and a trim goes down to three quarters of it.
        var cache = Cache(diskCacheSize: 3_000);
        var uris = Enumerable.Range(0, 4).Select(i => new Uri($"https://example.org/{i}.jpg")).ToArray();
        foreach (var uri in uris)
        {
            await cache.GetAsync(uri, TimeSpan.FromDays(1));
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        // Showing the oldest again makes it the most recently used.
        await cache.GetAsync(uris[0], TimeSpan.FromDays(1));
        cache.Trim();

        Assert.True(cache.Contains(uris[0]));
        Assert.False(cache.Contains(uris[1]));
        Assert.False(cache.Contains(uris[2]));
        Assert.True(cache.Contains(uris[3]));
    }

    [Fact]
    public void Trim_deletes_abandoned_temporary_files()
    {
        Directory.CreateDirectory(_directory);
        var abandoned = Path.Combine(_directory, "a.img.1.tmp");
        var current = Path.Combine(_directory, "b.img.2.tmp");
        File.WriteAllBytes(abandoned, [1]);
        File.WriteAllBytes(current, [1]);
        File.SetLastWriteTimeUtc(abandoned, _time.GetUtcNow().UtcDateTime.AddHours(-1));
        File.SetLastWriteTimeUtc(current, _time.GetUtcNow().UtcDateTime);

        Cache().Trim();

        Assert.False(File.Exists(abandoned));
        Assert.True(File.Exists(current));
    }

    [Fact]
    public async Task Clear_empties_the_directory()
    {
        var cache = Cache();
        await cache.GetAsync(Photo, TimeSpan.FromDays(1));

        await cache.ClearAsync();

        Assert.False(cache.Contains(Photo));
        await cache.GetAsync(Photo, TimeSpan.FromDays(1));
        Assert.Equal(2, _server.Requests);
    }

    sealed class FakeServer : HttpMessageHandler
    {
        int _requests;
        int _concurrent;
        int _maxConcurrent;

        public int Requests => Volatile.Read(ref _requests);

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public TaskCompletionSource? Gate { get; set; }

        public static byte[] Body(Uri uri) => Enumerable.Range(0, 1_000).Select(i => (byte)(i + uri.AbsolutePath.Length)).ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            var now = Interlocked.Increment(ref _concurrent);
            int seen;
            while ((seen = Volatile.Read(ref _maxConcurrent)) < now && Interlocked.CompareExchange(ref _maxConcurrent, now, seen) != seen)
            {
            }

            try
            {
                if (Gate is { } gate)
                    await gate.Task.WaitAsync(cancellationToken);

                return Status == HttpStatusCode.OK
                    ? new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body(request.RequestUri!)) }
                    : new HttpResponseMessage(Status) { ReasonPhrase = Status.ToString() };
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }
    }
}
