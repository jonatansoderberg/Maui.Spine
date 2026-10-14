using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// Usage kept in the process. It is lost on restart and not shared between instances, so budgets reset with
/// every deploy: right for tests and samples, wrong for a bill.
/// </summary>
public sealed class InMemoryAIUsageStore : IAIUsageStore
{
    private readonly ConcurrentQueue<AIUsageRecord> _records = new();
    private readonly ConcurrentDictionary<(string Period, string Scope), AIUsageTotals> _totals = new();
    private readonly ConcurrentDictionary<(string Period, string Scope, AIBudgetLevel Level), bool> _alerts = new();

    /// <summary>Every record kept, oldest first.</summary>
    public IReadOnlyCollection<AIUsageRecord> Records => _records;

    /// <inheritdoc />
    public Task RecordAsync(AIUsageRecord record, IReadOnlyCollection<AIPeriod> periods, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(periods);

        _records.Enqueue(record);

        foreach (var period in periods)
        foreach (var scope in AIUsageScope.Of(record))
        {
            _totals.AddOrUpdate(
                (period.Key, scope.ToString()),
                _ => new AIUsageTotals(record.Cost ?? 0, 1),
                (_, total) => new AIUsageTotals(total.Cost + (record.Cost ?? 0), total.Calls + 1));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<AIUsageTotals> GetTotalsAsync(AIUsageScope scope, AIPeriod period, CancellationToken cancellationToken = default) =>
        Task.FromResult(_totals.GetValueOrDefault((period.Key, scope.ToString()), AIUsageTotals.Zero));

    /// <inheritdoc />
    public async IAsyncEnumerable<AIUsageRecord> QueryAsync(
        DateTimeOffset from, DateTimeOffset to, string? feature = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var record in _records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.At < from || record.At >= to) continue;
            if (feature is not null && !string.Equals(record.Feature, feature, StringComparison.OrdinalIgnoreCase)) continue;

            yield return record;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryMarkAlertAsync(AIUsageScope scope, AIPeriod period, AIBudgetLevel level, CancellationToken cancellationToken = default) =>
        Task.FromResult(_alerts.TryAdd((period.Key, scope.ToString(), level), true));
}
