using System.Security.Cryptography;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTelemetry.Trace;
using Plugin.Maui.Spine.Server.AI;
using Plugin.Maui.Spine.Server.AI.Assets;
using Plugin.Maui.Spine.Server.Messaging;
using SpineMessagingSample;
using SpineMessagingSample.Api;

// Publishes every kind of message, and handles the "receipts" and "flaky" subscribers with pumps of its own. The
// Functions app handles "work". It also serves generated postcards (GET /ai/assets/Postcard/{city}), made by its own
// "spine-ai-assets" pump once an OpenAI key is set. Try it from send.http, and watch the Aspire dashboard's traces and logs.
var builder = WebApplication.CreateBuilder(args);

builder.AddSampleTelemetry();
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddAspNetCoreInstrumentation());
builder.Services.AddSampleMessaging(builder.Configuration);
builder.Services.AddSpineMessagePump("receipts", "flaky", GeneratedAssetMessagingExtensions.DefaultSubscriber);

// Without Ai:Providers:openai:ApiKey the feature is off: lookups answer 404 { "off": "notConfigured" } and order nothing.
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai => ai
    .Image("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetImageClient(model).AsIImageGenerator())
    .UseInMemoryUsageStore());
builder.Services.AddSpineAIAssets(builder.Configuration.GetSection("Ai"), assets =>
{
    assets.Asset<string, PostcardPrompt>("Postcard").Check<PostcardCheck>();
    assets.UseAzureBlobStore();
});

var app = builder.Build();

app.MapGet("/", (MessageTopology topology) => new
{
    Subscribers = topology.Subscribers.Select(s => new { s.Name, Lanes = s.Lanes.Select(l => l.Name), Types = s.Types.Select(t => t.Name) }),
});

app.MapGet("/health", () => "ok");

app.MapSpineAIAssets();

// A picture of the given size, at normal or high priority. Above about 46 KB on Storage (192 KB on Service Bus) it
// goes through blob storage.
app.MapPost("/images", async (IMessagePublisher publisher, int count = 1, int kilobytes = 100, MessagePriority priority = MessagePriority.Normal) =>
{
    var results = await publisher.PublishBatchAsync(
        Enumerable.Range(1, count).Select(i => new ResizeImage($"picture-{i}.png", Convert.ToBase64String(RandomNumberGenerator.GetBytes(kilobytes * 768)), 800)),
        new PublishOptions { Priority = priority });
    return results;
});

app.MapPost("/index", async (IMessagePublisher publisher, string reason = "nightly") =>
    await publisher.PublishAsync(new RebuildIndex(reason), new PublishOptions { Priority = MessagePriority.Low }));

app.MapPost("/receipts", async (IMessagePublisher publisher, string order = "order-1", int delaySeconds = 30) =>
    await publisher.PublishAsync(new SendReceipt(order, "customer@example.com"), new PublishOptions
    {
        Delay = TimeSpan.FromSeconds(delaySeconds),
        DeduplicationId = $"receipt-{order}",
    }));

app.MapDelete("/receipts/{messageId}", async (IMessagePublisher publisher, string messageId, DateTimeOffset due) =>
{
    await publisher.CancelAsync(messageId, due);
    return Results.NoContent();
});

app.MapPost("/flaky", async (IMessagePublisher publisher, int failTimes = 2) =>
    await publisher.PublishAsync(new FlakyJob(Guid.NewGuid().ToString("N")[..8], failTimes)));

app.MapGet("/failed", async (IFailedMessages failed, string? subscriber) =>
{
    var list = new List<object>();
    await foreach (var message in failed.ListAsync(subscriber))
        list.Add(new { message.Subscriber, message.MessageId, message.Type, message.Attempts, message.FailedAt, message.Error });
    return list;
});

app.MapPost("/failed/{subscriber}/{messageId}/replay", async (IFailedMessages failed, string subscriber, string messageId) =>
{
    await foreach (var message in failed.ListAsync(subscriber))
    {
        if (message.MessageId == messageId)
        {
            await failed.ReplayAsync(message);
            return Results.Accepted();
        }
    }
    return Results.NotFound();
});

app.MapPost("/janitor", async (MessagingJanitor janitor) => await janitor.RunAsync());

app.Run();
