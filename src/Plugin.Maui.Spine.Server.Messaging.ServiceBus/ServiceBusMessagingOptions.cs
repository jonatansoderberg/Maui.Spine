using Azure.Core;

namespace Plugin.Maui.Spine.Server.Messaging.ServiceBus;

/// <summary>How <see cref="ServiceBusMessagingExtensions.UseServiceBus"/> reaches the namespace.</summary>
public sealed class ServiceBusMessagingOptions
{
    /// <summary>
    /// The connection name: <c>ConnectionStrings:{name}</c> (what Aspire sets), or a setting of that name. A connection
    /// string, or the namespace (<c>my-app.servicebus.windows.net</c>) used with <see cref="Credential"/>.
    /// <c>servicebus</c> by default.
    /// </summary>
    public string ConnectionName { get; set; } = "servicebus";

    /// <summary>The blob container for large payloads, failed messages and markers. <c>spine-messaging</c> by default.</summary>
    public string Container { get; set; } = "spine-messaging";

    /// <summary>The topic every message is published to. <c>spine-messages</c> by default.</summary>
    public string TopicName { get; set; } = "spine-messages";

    /// <summary>
    /// Whether the host creates the topic, the subscriptions and their rules at startup, and brings the rules up to date.
    /// On by default. Turn it off where the identity has no Manage right and infrastructure as code creates them.
    /// </summary>
    public bool Provision { get; set; } = true;

    /// <summary>
    /// Where the emulator takes administration calls, used for provisioning when the connection string has
    /// <c>UseDevelopmentEmulator=true</c>: <c>host:port</c> or a URL. The emulator listens on port 5300, not on its AMQP
    /// port; unset, that port on the connection's host. Under Aspire, which maps the port, pass the emulator's
    /// <c>emulatorhealth</c> endpoint: <c>.WithEnvironment("…", servicebus.GetEndpoint("emulatorhealth"))</c>.
    /// </summary>
    public string? EmulatorManagementEndpoint { get; set; }

    /// <summary>The identity used with a namespace name, such as <c>DefaultAzureCredential</c>. Not needed with a connection string.</summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>The largest payload carried inline. 192 KB by default, under the Standard tier's 256 KB; raise it on Premium.</summary>
    public int MaxBodyBytes { get; set; } = 192 * 1024;

    /// <summary>Deliveries before Service Bus dead-letters a message that keeps crashing its handler. 10 by default.</summary>
    public int MaxDeliveryCount { get; set; } = 10;

    /// <summary>How long a received message stays locked; the pump renews it at half time. One minute by default.</summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How long a receive waits for a message before the pump moves to the next lane. 200 ms by default.</summary>
    public TimeSpan ReceiveWait { get; set; } = TimeSpan.FromMilliseconds(200);
}
