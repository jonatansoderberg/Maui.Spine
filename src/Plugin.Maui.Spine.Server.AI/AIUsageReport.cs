namespace Plugin.Maui.Spine.Server.AI;

/// <summary>What <c>GET /ai/usage</c> answers.</summary>
/// <param name="From">The first instant covered.</param>
/// <param name="To">The instant the report ends, excluded.</param>
/// <param name="Currency">The app's currency.</param>
/// <param name="Total">Everything in the range.</param>
/// <param name="Rows">One row per day, feature and model, newest day first.</param>
/// <param name="TopCallers">The ten callers who cost the most.</param>
/// <param name="Unpriced">Models that ran without a price row, as <c>provider/model</c>.</param>
public sealed record AIUsageReport(
    DateTimeOffset From, DateTimeOffset To, string Currency, AIUsageSum Total,
    IReadOnlyList<AIUsageReportRow> Rows, IReadOnlyList<AIUsageReportCaller> TopCallers, IReadOnlyList<string> Unpriced)
{
    private const int TopCallerCount = 10;

    internal static AIUsageReport Of(IReadOnlyList<AIUsageRecord> records, DateTimeOffset from, DateTimeOffset to, string currency, TimeZoneInfo zone)
    {
        var rows = records
            .GroupBy(r => (Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.At, zone).Date), r.Feature, r.Provider, r.Model))
            .Select(g => new AIUsageReportRow(g.Key.Day, g.Key.Feature, g.Key.Provider, g.Key.Model, AIUsageSum.Of(g)))
            .OrderByDescending(r => r.Day).ThenBy(r => r.Feature).ThenBy(r => r.Model)
            .ToList();

        var callers = records
            .GroupBy(r => r.Caller)
            .Select(g => new AIUsageReportCaller(g.Key, AIUsageSum.Of(g)))
            .OrderByDescending(c => c.Usage.Cost).ThenByDescending(c => c.Usage.Calls)
            .Take(TopCallerCount)
            .ToList();

        var unpriced = records
            .Where(r => r.Cost is null)
            .Select(r => $"{r.Provider}/{r.Model}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new(from, to, currency, AIUsageSum.Of(records), rows, callers, unpriced);
    }
}

/// <summary>A sum of calls.</summary>
/// <param name="Calls">Calls made.</param>
/// <param name="Failed">Calls that ended in an error.</param>
/// <param name="Incomplete">Calls whose provider reported no usage, so their tokens are estimates.</param>
/// <param name="InputTokens">Input tokens, cached ones included.</param>
/// <param name="CachedInputTokens">Input tokens read from the provider's cache.</param>
/// <param name="OutputTokens">Output tokens, reasoning included.</param>
/// <param name="ReasoningTokens">Reasoning tokens.</param>
/// <param name="Images">Images generated.</param>
/// <param name="Cost">What the priced calls cost.</param>
public sealed record AIUsageSum(
    long Calls, long Failed, long Incomplete, long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningTokens, long Images, decimal Cost)
{
    internal static AIUsageSum Of(IEnumerable<AIUsageRecord> records)
    {
        long calls = 0, failed = 0, incomplete = 0, input = 0, cached = 0, output = 0, reasoning = 0, images = 0;
        decimal cost = 0;

        foreach (var r in records)
        {
            calls++;
            if (r.Error is not null) failed++;
            if (!r.Complete) incomplete++;
            input += r.InputTokens;
            cached += r.CachedInputTokens;
            output += r.OutputTokens;
            reasoning += r.ReasoningTokens;
            images += r.Images;
            cost += r.Cost ?? 0;
        }

        return new(calls, failed, incomplete, input, cached, output, reasoning, images, cost);
    }
}

/// <summary>One day, feature and model.</summary>
/// <param name="Day">The day, in the app's time zone.</param>
/// <param name="Feature">The feature.</param>
/// <param name="Provider">The provider.</param>
/// <param name="Model">The model.</param>
/// <param name="Usage">The sum.</param>
public sealed record AIUsageReportRow(DateOnly Day, string Feature, string Provider, string Model, AIUsageSum Usage);

/// <summary>One caller.</summary>
/// <param name="Caller">The caller's <see cref="AICaller.Key"/>.</param>
/// <param name="Usage">The sum.</param>
public sealed record AIUsageReportCaller(string Caller, AIUsageSum Usage);
