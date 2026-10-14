using System.Net;
using System.Net.Sockets;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Plugin.Maui.Spine.Server.AI.Assets.Tests;

/// <summary>A fact that runs only when Azurite's blob service listens on 127.0.0.1:10000.</summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    public const string ConnectionString = "UseDevelopmentStorage=true";

    private static readonly bool Up = Listening(10000);

    public AzuriteFactAttribute()
    {
        if (!Up)
            Skip = "No Azurite on 127.0.0.1:10000. Run `docker run -p 10000-10002:10000-10002 mcr.microsoft.com/azure-storage/azurite`.";
    }

    private static bool Listening(int port)
    {
        try
        {
            using var socket = new TcpClient();
            return socket.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromSeconds(1)) && socket.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public sealed class AzureBlobStoreTests : IAsyncLifetime
{
    private readonly string _container = $"assets-{Guid.NewGuid():N}"[..30];
    private readonly BlobServiceClient _service = new(AzuriteFactAttribute.ConnectionString);
    private AzureBlobGeneratedAssetStore _store = null!;

    public Task InitializeAsync()
    {
        _store = new AzureBlobGeneratedAssetStore(_service, new AzureBlobAssetStoreOptions { Container = _container }, NullLogger.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _service.GetBlobContainerClient(_container).DeleteIfExistsAsync();
            await _service.GetBlobContainerClient($"{_container}-failures").DeleteIfExistsAsync();
        }
        catch (Exception)
        {
            // Azurite is not running; the facts were skipped.
        }
    }

    [AzuriteFact]
    public async Task An_image_is_archived_publicly_and_failures_privately()
    {
        await _store.ProvisionAsync(default);

        Assert.Null(await _store.GetAsync("arena/v1/e1/summer.png", default));
        var put = await _store.PutAsync("arena/v1/e1/summer.png", new byte[] { 1, 2, 3 }, "image/png", default);
        var got = await _store.GetAsync("arena/v1/e1/summer.png", default);

        Assert.NotNull(got);
        Assert.Equal(put.LastModified, got.LastModified);
        Assert.EndsWith($"/{_container}/arena/v1/e1/summer.png", got.Url.ToString());

        // Anonymous, as the app fetches it.
        using var http = new HttpClient();
        var fetched = await http.GetAsync(got.Url);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, await fetched.Content.ReadAsByteArrayAsync());

        var failures = await _service.GetBlobContainerClient($"{_container}-failures").GetAccessPolicyAsync();
        Assert.Equal(PublicAccessType.None, failures.Value.BlobPublicAccess);
    }

    [AzuriteFact]
    public async Task A_failure_is_noted_read_and_removed()
    {
        await _store.ProvisionAsync(default);
        var failure = new AssetFailureRecord(GeneratedAssetFailure.Check, AssetHost.Start, 3, AssetHost.Start.AddHours(1));

        await _store.SetFailureAsync("arena/v1/e1/summer", failure, default);
        Assert.Equal(failure, await _store.GetFailureAsync("arena/v1/e1/summer", default));

        await _store.DeleteFailureAsync("arena/v1/e1/summer", default);
        Assert.Null(await _store.GetFailureAsync("arena/v1/e1/summer", default));
    }

    [AzuriteFact]
    public async Task A_public_base_url_replaces_the_blobs_own()
    {
        var store = new AzureBlobGeneratedAssetStore(_service,
            new AzureBlobAssetStoreOptions { Container = _container, PublicBaseUrl = new Uri("https://cdn.example.com/pictures") }, NullLogger.Instance);
        await store.ProvisionAsync(default);

        var put = await store.PutAsync("arena/v1/e1/summer.png", new byte[] { 1 }, "image/png", default);

        Assert.Equal("https://cdn.example.com/pictures/arena/v1/e1/summer.png", put.Url.ToString());
    }
}
