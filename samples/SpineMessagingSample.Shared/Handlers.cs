using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging;

namespace SpineMessagingSample;

public sealed class ResizeImageHandler(ILogger<ResizeImageHandler> logger) : IMessageHandler<ResizeImage>
{
    public async Task HandleAsync(ResizeImage message, MessageContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken);
        logger.LogInformation("Resized {Name} ({Size} KB) to {Width} px at {Priority} priority.",
            message.Name, message.Picture.Length / 1024, message.Width, context.Priority);
    }
}

public sealed class RebuildIndexHandler(ILogger<RebuildIndexHandler> logger) : IMessageHandler<RebuildIndex>
{
    public async Task HandleAsync(RebuildIndex message, MessageContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(2000, cancellationToken);
        logger.LogInformation("Rebuilt the index ({Reason}), published {Waited:N1} s ago.",
            message.Reason, (DateTimeOffset.UtcNow - context.EnqueuedAt).TotalSeconds);
    }
}

public sealed class SendReceiptHandler(ILogger<SendReceiptHandler> logger) : IMessageHandler<SendReceipt>
{
    public Task HandleAsync(SendReceipt message, MessageContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Sent the receipt for {OrderId} to {Email}; it was due {DeliverAt:T}.", message.OrderId, message.Email, context.DeliverAt);
        return Task.CompletedTask;
    }
}

public sealed class FlakyJobHandler(ILogger<FlakyJobHandler> logger) : IMessageHandler<FlakyJob>
{
    public Task HandleAsync(FlakyJob message, MessageContext context, CancellationToken cancellationToken)
    {
        if (context.Attempt <= message.FailTimes)
            throw new InvalidOperationException($"Flaky job {message.Id} fails attempt {context.Attempt} as planned.");

        logger.LogInformation("Flaky job {Id} succeeded on attempt {Attempt}.", message.Id, context.Attempt);
        return Task.CompletedTask;
    }
}
