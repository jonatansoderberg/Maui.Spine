using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>Where <see cref="AzureBlobGeneratedAssetStore"/> keeps images.</summary>
public sealed class AzureBlobAssetStoreOptions
{
    /// <summary>
    /// The blob service's connection name, read as <c>ConnectionStrings:{name}</c>, a setting <c>{name}</c> or
    /// <c>{name}:blobServiceUri</c> with <see cref="Credential"/>. <c>blobs</c> by default, as Spine messaging and
    /// Aspire's <c>AddBlobs("blobs")</c> name it.
    /// </summary>
    public string ConnectionName { get; set; } = "blobs";

    /// <summary>The images' container, created with public read access if missing. Failures go in <c>{Container}-failures</c>, which is private.</summary>
    public string Container { get; set; } = "spine-ai-assets";

    /// <summary>A CDN or custom domain in front of the container. Unset, the blob's own URL.</summary>
    public Uri? PublicBaseUrl { get; set; }

    /// <summary>
    /// Whether the container is created with public read access for blobs. Turn it off when a CDN reads a private
    /// container; <see cref="PublicBaseUrl"/> then points at the CDN.
    /// </summary>
    public bool PublicAccess { get; set; } = true;

    /// <summary>The identity for a service URI, such as <c>new DefaultAzureCredential()</c>.</summary>
    public TokenCredential? Credential { get; set; }
}

/// <summary>Archives images in an Azure blob container that the app reads directly.</summary>
public sealed class AzureBlobGeneratedAssetStore : IGeneratedAssetStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BlobContainerClient _images;
    private readonly BlobContainerClient _failures;
    private readonly AzureBlobAssetStoreOptions _options;
    private readonly ILogger _logger;

    /// <summary>Creates the store over <paramref name="service"/>, with failures in a private container beside the images'.</summary>
    public AzureBlobGeneratedAssetStore(BlobServiceClient service, AzureBlobAssetStoreOptions options, ILogger logger)
    {
        _images = service.GetBlobContainerClient(options.Container);
        _failures = service.GetBlobContainerClient($"{options.Container}-failures");
        _options = options;
        _logger = logger;
    }

    internal static AzureBlobGeneratedAssetStore Create(IConfiguration configuration, AzureBlobAssetStoreOptions options, ILogger logger)
    {
        var value = configuration.GetConnectionString(options.ConnectionName)
            ?? configuration[options.ConnectionName]
            ?? configuration[$"{options.ConnectionName}:blobServiceUri"]
            ?? throw new InvalidOperationException(
                $"Generated assets found no storage connection named '{options.ConnectionName}'. Set ConnectionStrings:{options.ConnectionName} (Aspire's WithReference does), a setting '{options.ConnectionName}', or '{options.ConnectionName}:blobServiceUri' with a credential.");

        var service = Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? new BlobServiceClient(uri, options.Credential ?? throw new InvalidOperationException(
                $"The storage connection '{options.ConnectionName}' is a service URI ({uri}), which needs a credential. Set Credential in UseAzureBlobStore."))
            : new BlobServiceClient(value);

        return new AzureBlobGeneratedAssetStore(service, options, logger);
    }

    /// <inheritdoc />
    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _images.CreateIfNotExistsAsync(_options.PublicAccess ? PublicAccessType.Blob : PublicAccessType.None, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException e) when (e.ErrorCode == "PublicAccessNotPermitted")
        {
            throw new InvalidOperationException(
                $"The storage account does not allow public blob access, so the container '{_images.Name}' cannot be created for the app to read. Allow blob public access on the account, or set PublicAccess = false with a CDN in PublicBaseUrl.", e);
        }

        await _failures.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        if (_options.PublicAccess && _options.PublicBaseUrl is null)
        {
            // An existing private container answers every fetch with 403, and the app shows a placeholder forever.
            var policy = await _images.GetAccessPolicyAsync(cancellationToken: cancellationToken);
            if (policy.Value.BlobPublicAccess == PublicAccessType.None)
                _logger.LogError("Generated assets: the container '{Container}' is private, so the app cannot fetch its images. Set its access level to Blob, or put a CDN in PublicBaseUrl.", _images.Name);
        }
    }

    /// <inheritdoc />
    public async Task<StoredAsset?> GetAsync(string path, CancellationToken cancellationToken)
    {
        var blob = _images.GetBlobClient(path);
        try
        {
            var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            return new StoredAsset(UrlOf(blob), properties.Value.LastModified);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<StoredAsset> PutAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        var blob = _images.GetBlobClient(path);
        var info = await blob.UploadAsync(BinaryData.FromBytes(content), new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = "public, max-age=31536000, immutable" },
        }, cancellationToken);
        return new StoredAsset(UrlOf(blob), info.Value.LastModified);
    }

    /// <inheritdoc />
    public async Task<AssetFailureRecord?> GetFailureAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var content = await _failures.GetBlobClient(FailurePath(path)).DownloadContentAsync(cancellationToken);
            return content.Value.Content.ToObjectFromJson<AssetFailureRecord>(Json);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task SetFailureAsync(string path, AssetFailureRecord failure, CancellationToken cancellationToken) =>
        _failures.GetBlobClient(FailurePath(path)).UploadAsync(BinaryData.FromObjectAsJson(failure, Json), overwrite: true, cancellationToken);

    /// <inheritdoc />
    public Task DeleteFailureAsync(string path, CancellationToken cancellationToken) =>
        _failures.GetBlobClient(FailurePath(path)).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    private Uri UrlOf(BlobClient blob) =>
        _options.PublicBaseUrl is { } root ? new Uri(new Uri(root.ToString().TrimEnd('/') + "/"), blob.Name) : blob.Uri;

    private static string FailurePath(string path) => $"{path}.json";
}
