using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>Turns usage into money with a price row from configuration.</summary>
internal static class AIPricing
{
    private const decimal Million = 1_000_000m;

    /// <summary>
    /// What <paramref name="usage"/>, <paramref name="images"/>, realtime <paramref name="minutes"/> and <paramref name="characters"/> read
    /// aloud cost at <paramref name="price"/>, or
    /// <see langword="null"/> when there is no price.
    /// </summary>
    /// <remarks>
    /// Microsoft.Extensions.AI counts cached input inside <see cref="UsageDetails.InputTokenCount"/> and reasoning inside
    /// <see cref="UsageDetails.OutputTokenCount"/>, so each is taken out before the rest is priced as plain input or output.
    /// </remarks>
    public static decimal? Cost(AIPrice? price, UsageDetails? usage, int images, decimal minutes = 0, long characters = 0)
    {
        if (price is null) return null;

        var cost = images * (price.PerImage ?? 0) + (decimal)minutes * (price.PerMinute ?? 0) + characters * (price.PerMillionCharacters ?? 0) / Million;
        if (usage is null) return cost;

        var input = usage.InputTokenCount ?? 0;
        var cached = Math.Min(usage.CachedInputTokenCount ?? 0, input);
        var output = usage.OutputTokenCount ?? 0;
        var reasoning = Math.Min(usage.ReasoningTokenCount ?? 0, output);

        var inputPrice = price.InputPerMillion ?? 0;
        cost += (input - cached) * inputPrice / Million;
        cost += cached * (price.CachedInputPerMillion ?? inputPrice) / Million;

        var outputPrice = price.OutputPerMillion ?? 0;
        if (price.ReasoningPerMillion is { } reasoningPrice)
            cost += (output - reasoning) * outputPrice / Million + reasoning * reasoningPrice / Million;
        else
            cost += output * outputPrice / Million;

        if (usage.AdditionalCounts is { } additional)
        {
            foreach (var (name, count) in additional)
            {
                if (price.Additional.TryGetValue(name, out var perMillion))
                    cost += count * perMillion / Million;
            }
        }

        return cost;
    }
}
