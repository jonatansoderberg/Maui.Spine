using Azure.Core;
using Azure.Storage.Queues;

namespace Plugin.Maui.Spine.Server.Messaging.AzureStorage;

/// <summary>How <see cref="AzureStorageMessagingExtensions.UseAzureStorage"/> reaches the storage account.</summary>
public sealed class AzureStorageMessagingOptions
{
    /// <summary>
    /// The connection name of the queue service: <c>ConnectionStrings:{name}</c> (what Aspire sets), or a setting of that
    /// name. A connection string, or a service URI used with <see cref="Credential"/>. <c>queues</c> by default.
    /// </summary>
    public string QueuesConnectionName { get; set; } = "queues";

    /// <summary>The connection name of the blob service, read the same way. <c>blobs</c> by default.</summary>
    public string BlobsConnectionName { get; set; } = "blobs";

    /// <summary>The blob container for payloads, failed messages and markers. <c>spine-messaging</c> by default.</summary>
    public string Container { get; set; } = "spine-messaging";

    /// <summary>
    /// Put before every queue name, so that several apps can share an account (<c>orientera-</c>). Queue names, prefix
    /// included, are at most 63 characters, so keep it short.
    /// </summary>
    public string QueuePrefix { get; set; } = "";

    /// <summary>
    /// How messages are encoded in the queue. Base64 by default, which is what Azure Functions' queue trigger expects
    /// unless host.json sets <c>"messageEncoding": "none"</c>; then set <see cref="QueueMessageEncoding.None"/> here too
    /// and a message can carry a third more inline.
    /// </summary>
    public QueueMessageEncoding MessageEncoding { get; set; } = QueueMessageEncoding.Base64;

    /// <summary>How long a received message stays locked; the pump extends it at half time. 5 minutes by default.</summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The identity used with a service URI, such as <c>DefaultAzureCredential</c> or a managed identity. Not needed with
    /// a connection string.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>How many sends a batch runs at once. 16 by default.</summary>
    public int SendParallelism { get; set; } = 16;
}
