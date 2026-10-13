using Microsoft.Azure.Functions.Worker;
using Plugin.Maui.Spine.Server.Messaging;

namespace SpineMessagingSample.Functions;

/// <summary>Cleans up every hour: expired payloads, markers and old failed messages, and dead-lettered messages.</summary>
public sealed class JanitorFunction(MessagingJanitor janitor)
{
    [Function("MessagingJanitor")]
    public Task Run([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken ct) => janitor.RunAsync(ct);
}
