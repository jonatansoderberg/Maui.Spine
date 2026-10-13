using Azure.Core;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Plugin.Maui.Spine.Server.Messaging.AzureStorage;

/// <summary>Runs Spine messaging on Azure Storage.</summary>
public static class AzureStorageMessagingExtensions
{
    /// <summary>
    /// Uses Storage Queues as the transport and a blob container as the store. The connections are read from
    /// configuration by name: <c>ConnectionStrings:queues</c> and <c>ConnectionStrings:blobs</c> by default, which is what
    /// Aspire's <c>AddQueues("queues")</c> and <c>AddBlobs("blobs")</c> with <c>WithReference</c> provide.
    /// </summary>
    /// <param name="builder">The messaging declaration.</param>
    /// <param name="queues">The queue service's connection name.</param>
    /// <param name="blobs">The blob service's connection name.</param>
    /// <param name="configure">Prefix, container, encoding, visibility and credential.</param>
    public static MessagingBuilder UseAzureStorage(this MessagingBuilder builder, string queues = "queues", string blobs = "blobs",
        Action<AzureStorageMessagingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new AzureStorageMessagingOptions { QueuesConnectionName = queues, BlobsConnectionName = blobs };
        configure?.Invoke(options);

        builder.Services.AddSingleton(sp => new StorageQueueTransport(
            AzureStorageConnections.Queues(sp.GetRequiredService<IConfiguration>(), options.QueuesConnectionName, options.Credential,
                new QueueClientOptions { MessageEncoding = options.MessageEncoding }),
            options,
            sp.GetService<TimeProvider>()));

        return builder
            .UseTransport(sp => sp.GetRequiredService<StorageQueueTransport>())
            .UseBlobStore(options.BlobsConnectionName, options.Container, options.Credential);
    }

    /// <summary>
    /// Uses a blob container as the store for payloads, failed messages and markers, with any transport. Service Bus
    /// calls this; call it yourself after a transport of your own.
    /// </summary>
    /// <param name="builder">The messaging declaration.</param>
    /// <param name="blobs">The blob service's connection name, read like <see cref="UseAzureStorage"/>'s.</param>
    /// <param name="container">The container; created if missing.</param>
    /// <param name="credential">The identity for a service URI.</param>
    public static MessagingBuilder UseBlobStore(this MessagingBuilder builder, string blobs = "blobs", string container = "spine-messaging", TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(sp => new BlobMessageStore(
            AzureStorageConnections.Container(sp.GetRequiredService<IConfiguration>(), blobs, container, credential),
            sp.GetService<TimeProvider>()));
        return builder.UseStore(sp => sp.GetRequiredService<BlobMessageStore>());
    }
}
