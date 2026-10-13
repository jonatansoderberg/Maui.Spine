using System.Text.RegularExpressions;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging.AzureStorage;

namespace Plugin.Maui.Spine.Server.Messaging.ServiceBus;

/// <summary>Runs Spine messaging on Azure Service Bus.</summary>
public static partial class ServiceBusMessagingExtensions
{
    /// <summary>
    /// Uses Service Bus as the transport and a blob container as the store for large payloads, failed messages and
    /// markers. The connections are read from configuration by name: <c>ConnectionStrings:servicebus</c> and
    /// <c>ConnectionStrings:blobs</c> by default, which is what Aspire's <c>AddAzureServiceBus("servicebus")</c> and
    /// <c>AddBlobs("blobs")</c> with <c>WithReference</c> provide.
    /// </summary>
    /// <param name="builder">The messaging declaration.</param>
    /// <param name="connectionName">The Service Bus connection name.</param>
    /// <param name="blobs">The blob service's connection name.</param>
    /// <param name="configure">Topic, container, provisioning, limits and credential.</param>
    public static MessagingBuilder UseServiceBus(this MessagingBuilder builder, string connectionName = "servicebus", string blobs = "blobs",
        Action<ServiceBusMessagingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ServiceBusMessagingOptions { ConnectionName = connectionName };
        configure?.Invoke(options);

        builder.Services.AddSingleton(sp =>
        {
            var (client, admin) = Clients(sp.GetRequiredService<IConfiguration>(), options);
            return new ServiceBusTransport(client, admin, options, sp.GetRequiredService<ILogger<ServiceBusTransport>>(), sp.GetService<TimeProvider>());
        });

        return builder
            .UseTransport(sp => sp.GetRequiredService<ServiceBusTransport>())
            .UseBlobStore(blobs, options.Container, options.Credential);
    }

    private static (ServiceBusClient Client, ServiceBusAdministrationClient? Admin) Clients(IConfiguration configuration, ServiceBusMessagingOptions options)
    {
        var name = options.ConnectionName;
        var value = configuration.GetConnectionString(name)
            ?? configuration[name]
            ?? configuration[$"{name}:fullyQualifiedNamespace"]
            ?? throw new InvalidOperationException(
                $"Messaging found no Service Bus connection named '{name}'. Set ConnectionStrings:{name} (Aspire's WithReference does), a setting '{name}', or '{name}:fullyQualifiedNamespace' with a credential.");

        if (value.Contains("SharedAccessKey", StringComparison.OrdinalIgnoreCase))
        {
            var admin = !options.Provision
                ? null
                : value.Contains("UseDevelopmentEmulator=true", StringComparison.OrdinalIgnoreCase)
                    ? new ServiceBusAdministrationClient(EmulatorEndpoint().Replace(value, m => $"sb://{Management(options.EmulatorManagementEndpoint, m.Groups[1].Value)}"))
                    : new ServiceBusAdministrationClient(value);
            return (new ServiceBusClient(value), admin);
        }

        // A namespace, bare or as an endpoint, used with an identity.
        var host = EndpointHost().Match(value) is { Success: true } endpoint ? endpoint.Groups[1].Value
            : Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host
            : value;
        var credential = options.Credential ?? throw new InvalidOperationException(
            $"The Service Bus connection '{name}' names a namespace ({host}), which needs a credential. Set Credential in UseServiceBus, such as new DefaultAzureCredential().");
        return (new ServiceBusClient(host, credential), options.Provision ? new ServiceBusAdministrationClient(host, credential) : null);
    }

    private static string Management(string? endpoint, string host) => endpoint switch
    {
        null or "" => $"{host}:5300",
        _ when Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Host.Length > 0 => $"{uri.Host}:{uri.Port}",
        _ => endpoint,
    };

    [GeneratedRegex(@"sb://([^:/;]+)(:\d+)?", RegexOptions.IgnoreCase)]
    private static partial Regex EmulatorEndpoint();

    [GeneratedRegex(@"Endpoint=(?:sb|https)://([^:/;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointHost();
}
