using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>Prices a finished call and writes it to the store.</summary>
internal sealed class AIUsageMeter(AIFeatureRegistry registry, IAIUsageStore store, TimeProvider time, ILogger logger)
{
    private readonly ConcurrentDictionary<string, bool> _unpriced = new(StringComparer.OrdinalIgnoreCase);

    public TimeProvider Time => time;

    /// <summary>
    /// Records one call. Never throws: the call has already been paid for, and a store that is down must not turn
    /// an answer into an error. A failure to record is logged as an error instead.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <param name="caller">Who it was for.</param>
    /// <param name="model">The model it ran on.</param>
    /// <param name="started">When it started, from <see cref="TimeProvider.GetTimestamp"/>.</param>
    /// <param name="usage">What the provider reported, or <see langword="null"/>.</param>
    /// <param name="images">Images generated.</param>
    /// <param name="estimatedInput">Input tokens to record when the provider reported none.</param>
    /// <param name="error">The exception the call ended with.</param>
    /// <param name="minutes">Realtime minutes reserved, priced at <see cref="AIPrice.PerMinute"/>.</param>
    public async Task RecordAsync(
        AIFeature feature, AICaller caller, string model, long started, UsageDetails? usage, int images, long estimatedInput, Exception? error,
        int minutes = 0)
    {
        try
        {
            var snapshot = registry.Current;
            var complete = usage is not null || (images > 0 && error is null);
            var counted = usage ?? new UsageDetails { InputTokenCount = estimatedInput };
            var price = snapshot.PriceOf(feature.Provider, model);

            if (price is null && _unpriced.TryAdd($"{feature.Provider}/{model}", true))
                logger.LogWarning("AI usage: {Provider}/{Model} has no price under Ai:Prices; its calls are recorded without a cost.", feature.Provider, model);

            var now = time.GetUtcNow();
            var duration = time.GetElapsedTime(started);

            var record = new AIUsageRecord(
                At: now - duration,
                Feature: feature.Name,
                Provider: feature.Provider,
                Model: model,
                Caller: caller.Key,
                InputTokens: counted.InputTokenCount ?? 0,
                CachedInputTokens: counted.CachedInputTokenCount ?? 0,
                OutputTokens: counted.OutputTokenCount ?? 0,
                ReasoningTokens: counted.ReasoningTokenCount ?? 0,
                Images: images,
                Cost: AIPricing.Cost(price, counted, images, minutes),
                Complete: complete,
                Duration: duration,
                Error: error is null ? null : $"{error.GetType().Name}: {error.Message}");

            await store.RecordAsync(record, [AIPeriod.Day(record.At, snapshot.Zone), AIPeriod.Month(record.At, snapshot.Zone)], CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "AI usage: a call to '{Feature}' for {Caller} could not be recorded, so budgets do not see it.", feature.Name, caller.Key);
        }
    }

    /// <summary>Four characters a token: rough, but only used when the provider says nothing.</summary>
    public static long Estimate(IEnumerable<ChatMessage> messages) =>
        (messages.Sum(m => (long)m.Text.Length) + 3) / 4;
}
