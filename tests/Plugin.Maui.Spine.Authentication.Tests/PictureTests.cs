using System.Net;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Plugin.Maui.Spine.Authentication.Tests;

/// <summary>The signed-in user's picture: Google's address, or the provider's own (Microsoft's Graph), kept a day.</summary>
public sealed class PictureTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "spine-picture-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly List<string> _requests = [];
    private HttpStatusCode _status = HttpStatusCode.OK;
    private bool _offline;

    private PictureCache Cache() => new(new HttpClient(new Handler(this)), _directory, _time);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static SpineUser Google(string url = "https://lh3.googleusercontent.com/a/eva") => new("u-1", null, "Eva", "google", url);

    [Fact]
    public async Task Googles_address_is_fetched_once_and_kept()
    {
        var cache = Cache();

        Assert.Equal(Jpeg, await cache.GetAsync(Google(), null, default));
        Assert.Equal(Jpeg, await cache.GetAsync(Google(), null, default));
        Assert.Single(_requests);
    }

    [Fact]
    public async Task After_a_day_it_is_fetched_again()
    {
        var cache = Cache();
        await cache.GetAsync(Google(), null, default);

        File.SetLastWriteTimeUtc(Directory.GetFiles(_directory).Single(), (_time.GetUtcNow() - PictureCache.MaxAge - TimeSpan.FromMinutes(1)).UtcDateTime);
        await cache.GetAsync(Google(), null, default);

        Assert.Equal(2, _requests.Count);
    }

    [Fact]
    public async Task A_new_address_is_a_new_picture()
    {
        var cache = Cache();
        await cache.GetAsync(Google("https://lh3.googleusercontent.com/a/one"), null, default);
        await cache.GetAsync(Google("https://lh3.googleusercontent.com/a/two"), null, default);

        Assert.Equal(["https://lh3.googleusercontent.com/a/one", "https://lh3.googleusercontent.com/a/two"], _requests);
    }

    [Fact]
    public async Task No_picture_is_remembered_as_none()
    {
        _status = HttpStatusCode.NotFound;
        var cache = Cache();

        Assert.Null(await cache.GetAsync(Google(), null, default));
        Assert.Null(await cache.GetAsync(Google(), null, default));
        Assert.Single(_requests);
    }

    [Fact]
    public async Task Offline_keeps_the_last_picture_and_remembers_nothing()
    {
        var cache = Cache();
        await cache.GetAsync(Google(), null, default);
        File.SetLastWriteTimeUtc(Directory.GetFiles(_directory).Single(), DateTime.UtcNow.AddDays(-2));

        _offline = true;
        Assert.Equal(Jpeg, await cache.GetAsync(Google(), null, default));

        _offline = false;
        await cache.GetAsync(Google(), null, default);
        Assert.Equal(3, _requests.Count);
    }

    [Fact]
    public async Task Without_an_address_the_provider_gives_it()
    {
        var microsoft = new SpineUser("u-2", null, "Mo", "microsoft");
        var asked = 0;

        var picture = await Cache().GetAsync(microsoft, _ => { asked++; return Task.FromResult<byte[]?>(Jpeg); }, default);

        Assert.Equal(Jpeg, picture);
        Assert.Equal(1, asked);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Apple_and_password_have_none()
    {
        Assert.Null(await Cache().GetAsync(new SpineUser("u-3", "a@example.com", null, "apple"), null, default));
    }

    [Fact]
    public async Task Sign_out_forgets_every_picture()
    {
        var cache = Cache();
        await cache.GetAsync(Google(), null, default);

        cache.Clear();

        Assert.Empty(Directory.GetFiles(_directory));
    }

    private sealed class Handler(PictureTests test) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            test._requests.Add(request.RequestUri!.AbsoluteUri);
            if (test._offline) throw new HttpRequestException("No connection.");
            return Task.FromResult(new HttpResponseMessage(test._status) { Content = new ByteArrayContent(test._status == HttpStatusCode.OK ? Jpeg : []) });
        }
    }
}
