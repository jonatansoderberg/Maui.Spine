using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.AI;

/// <summary>What on-device calls to a feature used. They cost nothing, so there is no cost.</summary>
/// <param name="Calls">Calls made on the device.</param>
/// <param name="InputTokens">Input tokens, estimated at four characters a token when the model reported none.</param>
/// <param name="OutputTokens">Output tokens, estimated the same way.</param>
public sealed record AIDeviceUsage(long Calls, long InputTokens, long OutputTokens)
{
    /// <summary>Nothing used.</summary>
    public static AIDeviceUsage Zero { get; } = new(0, 0, 0);

    internal AIDeviceUsage Plus(AIDeviceUsage other) =>
        new(Calls + other.Calls, InputTokens + other.InputTokens, OutputTokens + other.OutputTokens);
}

/// <summary>
/// What features used on the device, by day in the device's time zone. It stays on the device: calls on the server are
/// metered there, by Spine.Server.AI.
/// </summary>
public interface IAIUsage
{
    /// <summary>What <paramref name="feature"/> used on the device on <paramref name="day"/>.</summary>
    AIDeviceUsage ForDay(string feature, DateOnly day);

    /// <summary>What <paramref name="feature"/> used on the device from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    AIDeviceUsage ForRange(string feature, DateOnly from, DateOnly to);

    /// <summary>What <paramref name="feature"/> used on the device today.</summary>
    AIDeviceUsage Today(string feature);
}

/// <summary>Where on-device usage is kept: <c>Preferences</c> in an app, memory in tests.</summary>
internal interface IAIDeviceUsageStore
{
    void Add(string feature, DateOnly day, AIDeviceUsage usage);

    AIDeviceUsage Get(string feature, DateOnly day);
}

internal sealed class InMemoryAIDeviceUsageStore : IAIDeviceUsageStore
{
    private readonly ConcurrentDictionary<(string, DateOnly), AIDeviceUsage> _days = new();

    public void Add(string feature, DateOnly day, AIDeviceUsage usage) =>
        _days.AddOrUpdate((feature.ToLowerInvariant(), day), usage, (_, sum) => sum.Plus(usage));

    public AIDeviceUsage Get(string feature, DateOnly day) =>
        _days.GetValueOrDefault((feature.ToLowerInvariant(), day), AIDeviceUsage.Zero);
}

internal sealed class AIUsage(SpineAIRuntime runtime) : IAIUsage
{
    public AIDeviceUsage ForDay(string feature, DateOnly day) => runtime.Usage.Get(feature, day);

    public AIDeviceUsage ForRange(string feature, DateOnly from, DateOnly to)
    {
        var sum = AIDeviceUsage.Zero;
        for (var day = from; day <= to; day = day.AddDays(1)) sum = sum.Plus(runtime.Usage.Get(feature, day));
        return sum;
    }

    public AIDeviceUsage Today(string feature) => ForDay(feature, Today(runtime.Time));

    internal static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}

/// <summary>Counts an on-device client's calls and tokens into the device's usage store.</summary>
internal sealed class DeviceMeteredChatClient(IChatClient inner, string feature, SpineAIRuntime runtime) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        var response = await base.GetResponseAsync(list, options, cancellationToken);
        Record(list, response.Usage, response.Text);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        UsageDetails? usage = null;
        var text = new System.Text.StringBuilder();
        var any = false;

        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(list, options, cancellationToken))
            {
                any = true;
                text.Append(update.Text);
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent { Details: { } details }) (usage ??= new()).Add(details);
                }

                yield return update;
            }
        }
        finally
        {
            // A call that failed before it answered falls back to the next source and is not counted here.
            if (any) Record(list, usage, text.ToString());
        }
    }

    private void Record(IList<ChatMessage> messages, UsageDetails? usage, string answer)
    {
        try
        {
            var input = usage?.InputTokenCount ?? Estimate(messages.Sum(m => (long)m.Text.Length));
            var output = usage?.OutputTokenCount ?? Estimate(answer.Length);
            runtime.Usage.Add(feature, AIUsage.Today(runtime.Time), new AIDeviceUsage(1, input, output));
        }
        catch (Exception e)
        {
            runtime.Logger.LogError(e, "AI feature '{Feature}': on-device usage could not be recorded.", feature);
        }
    }

    private static long Estimate(long characters) => (characters + 3) / 4;
}
