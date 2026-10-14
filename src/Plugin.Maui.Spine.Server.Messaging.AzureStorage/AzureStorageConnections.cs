using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;

namespace Plugin.Maui.Spine.Server.Messaging.AzureStorage;

/// <summary>Builds storage clients from configuration the way Aspire and Azure Functions name connections.</summary>
internal static class AzureStorageConnections
{
    public static QueueServiceClient Queues(IConfiguration configuration, string name, TokenCredential? credential, QueueClientOptions options)
    {
        var value = Find(configuration, name, "queueServiceUri");
        return AsServiceUri(value) is { } uri
            ? new QueueServiceClient(uri, credential ?? throw NeedsCredential(name, uri), options)
            : new QueueServiceClient(value, options);
    }

    public static BlobContainerClient Container(IConfiguration configuration, string name, string container, TokenCredential? credential)
    {
        var value = Find(configuration, name, "blobServiceUri");
        var service = AsServiceUri(value) is { } uri
            ? new BlobServiceClient(uri, credential ?? throw NeedsCredential(name, uri))
            : new BlobServiceClient(value);
        return service.GetBlobContainerClient(container);
    }

    private static string Find(IConfiguration configuration, string name, string uriKey) =>
        configuration.GetConnectionString(name)
        ?? configuration[name]
        ?? configuration[$"{name}:{uriKey}"]
        ?? throw new InvalidOperationException(
            $"Messaging found no storage connection named '{name}'. Set ConnectionStrings:{name} (Aspire's WithReference does), a setting '{name}', or '{name}:{uriKey}' with a credential.");

    private static Uri? AsServiceUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;

    private static InvalidOperationException NeedsCredential(string name, Uri uri) =>
        new($"The storage connection '{name}' is a service URI ({uri}), which needs a credential. Set Credential in UseAzureStorage or UseBlobStore, such as new DefaultAzureCredential().");
}
