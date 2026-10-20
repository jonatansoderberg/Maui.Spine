using System.Globalization;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>One model call, as metered.</summary>
/// <param name="At">When the call started.</param>
/// <param name="Feature">The feature's name.</param>
/// <param name="Provider">The provider's name under <c>Ai:Providers</c>.</param>
/// <param name="Model">The model id the call was priced by.</param>
/// <param name="Caller">The caller's <see cref="AICaller.Key"/>.</param>
/// <param name="InputTokens">Input tokens, cached ones included.</param>
/// <param name="CachedInputTokens">Input tokens read from the provider's cache.</param>
/// <param name="OutputTokens">Output tokens, reasoning included.</param>
/// <param name="ReasoningTokens">Reasoning tokens.</param>
/// <param name="Images">Images generated.</param>
/// <param name="Cost">What the call cost in the app's currency, or <see langword="null"/> when the model has no price.</param>
/// <param name="Complete">
/// <see langword="false"/> when the provider reported no usage, so the input is an estimate and the output unknown.
/// </param>
/// <param name="Duration">How long the call took, to the end of its stream.</param>
/// <param name="Error">The exception's type and message when the call failed.</param>
public sealed record AIUsageRecord(
    DateTimeOffset At, string Feature, string Provider, string Model, string Caller,
    long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningTokens,
    int Images, decimal? Cost, bool Complete, TimeSpan Duration, string? Error)
{
    /// <summary>
    /// Settles a call recorded earlier rather than being a call of its own: a relayed voice session's real minutes
    /// against the minutes it reserved (the cost can be negative), and its backend's tokens. Its cost counts toward the
    /// totals; it does not count as a call.
    /// </summary>
    public bool Adjustment { get; init; }
}

/// <summary>A day or a month.</summary>
public enum AIPeriodKind
{
    /// <summary>A calendar day.</summary>
    Day,

    /// <summary>A calendar month.</summary>
    Month,
}

/// <summary>A calendar day or month in the app's time zone, which totals and budgets are kept by.</summary>
/// <param name="Kind">Day or month.</param>
/// <param name="Key">The period's key: <c>d:2026-10-12</c> or <c>m:2026-10</c>.</param>
/// <param name="End">The instant the next period begins.</param>
public sealed record AIPeriod(AIPeriodKind Kind, string Key, DateTimeOffset End)
{
    /// <summary>The day <paramref name="at"/> falls on in <paramref name="zone"/>.</summary>
    public static AIPeriod Day(DateTimeOffset at, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var start = local.Date;
        return new(AIPeriodKind.Day, $"d:{start:yyyy-MM-dd}", StartOf(start.AddDays(1), zone));
    }

    /// <summary>The month <paramref name="at"/> falls in, in <paramref name="zone"/>.</summary>
    public static AIPeriod Month(DateTimeOffset at, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var start = new DateTime(local.Year, local.Month, 1);
        return new(AIPeriodKind.Month, $"m:{start.ToString("yyyy-MM", CultureInfo.InvariantCulture)}", StartOf(start.AddMonths(1), zone));
    }

    private static DateTimeOffset StartOf(DateTime localMidnight, TimeZoneInfo zone)
    {
        // A midnight that a clock change skips does not exist; the period then starts an hour later.
        while (zone.IsInvalidTime(localMidnight)) localMidnight = localMidnight.AddHours(1);
        return new DateTimeOffset(localMidnight, zone.GetUtcOffset(localMidnight));
    }

    /// <inheritdoc />
    public override string ToString() => Key;
}

/// <summary>
/// Whose spending a total counts: the whole app, one feature, or one caller of one feature. <c>*</c> stands
/// for all.
/// </summary>
/// <param name="Feature">The feature's name, or <c>*</c>.</param>
/// <param name="Caller">The caller's <see cref="AICaller.Key"/>, or <c>*</c>.</param>
public sealed record AIUsageScope(string Feature, string Caller)
{
    /// <summary>Everything the app spends.</summary>
    public static AIUsageScope App { get; } = new("*", "*");

    /// <summary>Everything one feature spends.</summary>
    public static AIUsageScope ForFeature(string feature) => new(feature, "*");

    /// <summary>What one caller spends on one feature.</summary>
    public static AIUsageScope ForCaller(string feature, AICaller caller) => new(feature, caller.Key);

    /// <summary>The three scopes a record counts toward.</summary>
    internal static AIUsageScope[] Of(AIUsageRecord record) =>
        [App, ForFeature(record.Feature), new(record.Feature, record.Caller)];

    /// <inheritdoc />
    public override string ToString() => $"{Feature}|{Caller}";
}

/// <summary>A running total.</summary>
/// <param name="Cost">What was spent. Unpriced calls add nothing.</param>
/// <param name="Calls">Calls made, priced or not.</param>
public sealed record AIUsageTotals(decimal Cost, long Calls)
{
    /// <summary>Nothing spent.</summary>
    public static AIUsageTotals Zero { get; } = new(0, 0);
}

/// <summary>A budget's two levels.</summary>
public enum AIBudgetLevel
{
    /// <summary>Spending passed the soft limit. The feature still runs.</summary>
    Soft,

    /// <summary>Spending reached the hard limit. The feature is off for the rest of the period.</summary>
    Hard,
}

/// <summary>
/// Where usage is kept: every call, and running totals per period that budgets are checked against.
/// </summary>
/// <remarks>
/// A budget check is a few <see cref="GetTotalsAsync"/> calls before every model call, so totals must be
/// cheap to read. <see cref="InMemoryAIUsageStore"/> shows the whole contract in a few dozen lines.
/// </remarks>
public interface IAIUsageStore
{
    /// <summary>
    /// Keeps <paramref name="record"/> and adds it to the totals of each period in <paramref name="periods"/>, for
    /// the app, the record's feature, and the record's caller of that feature.
    /// </summary>
    /// <param name="record">The call.</param>
    /// <param name="periods">The day and month it counts toward.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task RecordAsync(AIUsageRecord record, IReadOnlyCollection<AIPeriod> periods, CancellationToken cancellationToken = default);

    /// <summary>The total for <paramref name="scope"/> in <paramref name="period"/>, zero when nothing was recorded.</summary>
    /// <param name="scope">The app, a feature, or a caller of a feature.</param>
    /// <param name="period">The day or month.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<AIUsageTotals> GetTotalsAsync(AIUsageScope scope, AIPeriod period, CancellationToken cancellationToken = default);

    /// <summary>The calls made from <paramref name="from"/> up to <paramref name="to"/>.</summary>
    /// <param name="from">The first instant, included.</param>
    /// <param name="to">The last instant, excluded.</param>
    /// <param name="feature">Only this feature, or all when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    IAsyncEnumerable<AIUsageRecord> QueryAsync(DateTimeOffset from, DateTimeOffset to, string? feature = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notes that <paramref name="level"/> was reached for <paramref name="scope"/> in <paramref name="period"/>.
    /// Returns <see langword="true"/> only the first time, across every instance sharing the store, so the owner
    /// is told once.
    /// </summary>
    /// <param name="scope">The app or a feature.</param>
    /// <param name="period">The day or month.</param>
    /// <param name="level">Soft or hard.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<bool> TryMarkAlertAsync(AIUsageScope scope, AIPeriod period, AIBudgetLevel level, CancellationToken cancellationToken = default);
}
