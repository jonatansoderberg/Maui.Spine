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
    /// <param name="minutes">Realtime minutes reserved, or a transcription's audio minutes, priced at <see cref="AIPrice.PerMinute"/>.</param>
    /// <param name="characters">Characters read aloud, priced at <see cref="AIPrice.PerMillionCharacters"/>.</param>
    public async Task RecordAsync(
        AIFeature feature, AICaller caller, string model, long started, UsageDetails? usage, int images, long estimatedInput, Exception? error,
        decimal minutes = 0, long characters = 0, bool measured = false)
    {
        try
        {
            var snapshot = registry.Current;
            // Realtime minutes are reserved ahead and so an estimate; a transcription's minutes are measured from its audio.
            var complete = usage is not null || ((images > 0 || characters > 0 || measured) && error is null);
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
                Cost: AIPricing.Cost(price, counted, images, minutes, characters),
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

    /// <summary>
    /// Records what settles an earlier call (<see cref="AIUsageRecord.Adjustment"/>): <paramref name="minutes"/> used against
    /// <paramref name="reserved"/>, which can make the cost negative, and tokens the session's backend used. Never throws.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <param name="caller">Who it was for.</param>
    /// <param name="model">The model the record is priced by.</param>
    /// <param name="usage">Tokens, or <see langword="null"/> for minutes only.</param>
    /// <param name="minutes">Minutes used.</param>
    /// <param name="reserved">Minutes reserved before, to take off again.</param>
    /// <param name="duration">How long the session ran.</param>
    public async Task SettleAsync(AIFeature feature, AICaller caller, string model, UsageDetails? usage, decimal minutes, decimal reserved, TimeSpan duration)
    {
        try
        {
            var snapshot = registry.Current;
            var price = snapshot.PriceOf(feature.Provider, model);

            if (price is null && _unpriced.TryAdd($"{feature.Provider}/{model}", true))
                logger.LogWarning("AI usage: {Provider}/{Model} has no price under Ai:Prices; its calls are recorded without a cost.", feature.Provider, model);

            var counted = usage ?? new UsageDetails();
            var now = time.GetUtcNow();
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
                Images: 0,
                Cost: AIPricing.Cost(price, usage, 0, minutes - reserved),
                Complete: true,
                Duration: duration,
                Error: null)
            {
                Adjustment = true,
            };

            await store.RecordAsync(record, [AIPeriod.Day(record.At, snapshot.Zone), AIPeriod.Month(record.At, snapshot.Zone)], CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "AI usage: a session of '{Feature}' for {Caller} could not be settled, so budgets keep its reservation.", feature.Name, caller.Key);
        }
    }

    /// <summary>Four characters a token: rough, but only used when the provider says nothing.</summary>
    public static long Estimate(IEnumerable<ChatMessage> messages) =>
        (messages.Sum(m => (long)m.Text.Length) + 3) / 4;
}
