using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Plugin.Maui.Spine.Server.AI.Assets;
using Plugin.Maui.Spine.Server.Messaging;
using Plugin.Maui.Spine.Server.Messaging.AzureStorage;
using Plugin.Maui.Spine.Server.Messaging.ServiceBus;

namespace SpineMessagingSample;

/// <summary>The one messaging declaration the API and the Functions app share.</summary>
public static class SampleMessaging
{
    public static IServiceCollection AddSampleMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSpineMessaging(m =>
        {
            // The AppHost picks the transport; both run the same topology.
            if (configuration["Messaging:Transport"] == "servicebus")
                m.UseServiceBus(configure: o => o.EmulatorManagementEndpoint = configuration["Messaging:ServiceBusManagement"]);
            else
                m.UseAzureStorage();

            m.Message<ResizeImage>("resize-image");
            m.Message<RebuildIndex>("rebuild-index");
            m.Message<SendReceipt>("send-receipt");
            m.Message<FlakyJob>("flaky-job");

            // Handled in the Functions app: one trigger per lane.
            m.Subscriber("work", s => s.Handles<ResizeImage>().Handles<RebuildIndex>().Prioritized());

            // Handled by pumps in the API.
            m.Subscriber("receipts", s => s.Handles<SendReceipt>());
            m.Subscriber("flaky", s => s.Handles<FlakyJob>().Retry(3, first: TimeSpan.FromSeconds(2)));

            // Generated postcards: ordered and made by the API (Plugin.Maui.Spine.Server.AI.Assets).
            m.AddGeneratedAssets();
        });

        services.AddMessageHandler<ResizeImage, ResizeImageHandler>();
        services.AddMessageHandler<RebuildIndex, RebuildIndexHandler>();
        services.AddMessageHandler<SendReceipt, SendReceiptHandler>();
        services.AddMessageHandler<FlakyJob, FlakyJobHandler>();
        return services;
    }

    /// <summary>Sends logs, traces and metrics to the Aspire dashboard, messaging's included.</summary>
    /// <param name="builder">The host.</param>
    /// <param name="logs">
    /// Whether to export logs. The Functions host already forwards its worker's logs, so the Functions app passes
    /// <see langword="false"/> to avoid every line twice.
    /// </param>
    public static IHostApplicationBuilder AddSampleTelemetry(this IHostApplicationBuilder builder, bool logs = true)
    {
        if (logs)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });
        }

        var telemetry = builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(MessagingTelemetry.Name, builder.Environment.ApplicationName))
            .WithMetrics(metrics => metrics.AddMeter(MessagingTelemetry.Name));

        if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            telemetry.UseOtlpExporter();

        return builder;
    }
}
