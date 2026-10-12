using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Azure;
using Azure.Data.Tables;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// Usage in Azure Table Storage. Every call is a row in <c>{prefix}Usage</c>, partitioned by its UTC day.
/// Running totals are rows in <c>{prefix}Totals</c>, partitioned by period, so a budget check is a handful of
/// point reads.
/// </summary>
/// <param name="connectionString">The storage account; <c>UseDevelopmentStorage=true</c> for Azurite.</param>
/// <param name="prefix">Prepended to both table names, so several apps can share an account.</param>
public sealed class AzureTableAIUsageStore(string connectionString, string prefix = "SpineAI") : IAIUsageStore
{
    private const int MaxAttempts = 20;

    private readonly TableClient _usage = new(connectionString, $"{prefix}Usage");
    private readonly TableClient _totals = new(connectionString, $"{prefix}Totals");
    private readonly SemaphoreSlim _created = new(1, 1);
    private bool _tablesExist;

    /// <inheritdoc />
    public async Task RecordAsync(AIUsageRecord record, IReadOnlyCollection<AIPeriod> periods, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(periods);
        await EnsureTablesAsync(cancellationToken);

        await _usage.AddEntityAsync(UsageRow.From(record), cancellationToken);

        var scopes = AIUsageScope.Of(record);
        foreach (var period in periods)
            await IncrementAsync(period, scopes, record.Cost ?? 0, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AIUsageTotals> GetTotalsAsync(AIUsageScope scope, AIPeriod period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(period);
        await EnsureTablesAsync(cancellationToken);

        return await ReadTotalAsync(period, scope, cancellationToken) is { } row ? row.ToTotals() : AIUsageTotals.Zero;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AIUsageRecord> QueryAsync(
        DateTimeOffset from, DateTimeOffset to, string? feature = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureTablesAsync(cancellationToken);

        for (var day = from.UtcDateTime.Date; day < to.UtcDateTime; day = day.AddDays(1))
        {
            var partition = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            await foreach (var row in _usage.QueryAsync<UsageRow>(r => r.PartitionKey == partition, cancellationToken: cancellationToken))
            {
                var record = row.ToRecord();
                if (record.At < from || record.At >= to) continue;
                if (feature is not null && !string.Equals(record.Feature, feature, StringComparison.OrdinalIgnoreCase)) continue;

                yield return record;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryMarkAlertAsync(AIUsageScope scope, AIPeriod period, AIBudgetLevel level, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(period);
        await EnsureTablesAsync(cancellationToken);

        try
        {
            await _totals.AddEntityAsync(new TableEntity(Key(period.Key), Key($"alert|{level}|{scope}")), cancellationToken);
            return true;
        }
        catch (RequestFailedException e) when (e.Status == 409)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds one call to the period's three totals in a single transaction. Several instances can record at once,
    /// so the rows are read with their ETags and the write fails if another got there first; it is then read and
    /// tried again.
    /// </summary>
    private async Task IncrementAsync(AIPeriod period, AIUsageScope[] scopes, decimal cost, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var actions = new List<TableTransactionAction>(scopes.Length);

            foreach (var scope in scopes)
            {
                if (await ReadTotalAsync(period, scope, cancellationToken) is { } row)
                {
                    row.Add(cost);
                    actions.Add(new TableTransactionAction(TableTransactionActionType.UpdateReplace, row, row.ETag));
                }
                else
                {
                    actions.Add(new TableTransactionAction(TableTransactionActionType.Add, TotalRow.New(period, scope, cost)));
                }
            }

            try
            {
                await _totals.SubmitTransactionAsync(actions, cancellationToken);
                return;
            }
            catch (TableTransactionFailedException e) when (e.Status is 409 or 412 && attempt < MaxAttempts)
            {
                await Task.Delay(Random.Shared.Next(5, 20 * attempt), cancellationToken);
            }
        }
    }

    private async Task<TotalRow?> ReadTotalAsync(AIPeriod period, AIUsageScope scope, CancellationToken cancellationToken)
    {
        var found = await _totals.GetEntityIfExistsAsync<TotalRow>(Key(period.Key), Key(scope.ToString()), cancellationToken: cancellationToken);
        return found.HasValue ? found.Value : null;
    }

    private async Task EnsureTablesAsync(CancellationToken cancellationToken)
    {
        if (_tablesExist) return;

        await _created.WaitAsync(cancellationToken);
        try
        {
            if (_tablesExist) return;
            await _usage.CreateIfNotExistsAsync(cancellationToken);
            await _totals.CreateIfNotExistsAsync(cancellationToken);
            _tablesExist = true;
        }
        finally
        {
            _created.Release();
        }
    }

    /// <summary>
    /// <paramref name="value"/> made safe as a partition or row key. Caller ids come from tokens and headers, and
    /// Table Storage refuses <c>/ \ # ?</c> and control characters in keys; those, and <c>%</c>, are escaped.
    /// </summary>
    internal static string Key(string value)
    {
        if (!value.Any(NeedsEscape)) return value;

        var key = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            if (NeedsEscape(c)) key.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            else key.Append(c);
        }

        return key.ToString();

        static bool NeedsEscape(char c) => c is '/' or '\\' or '#' or '?' or '%' || char.IsControl(c);
    }

    internal sealed class TotalRow : ITableEntity
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Table Storage has no decimal, and a double drifts when thousands of tiny costs are summed.
        public string Cost { get; set; } = "0";
        public long Calls { get; set; }

        internal static TotalRow New(AIPeriod period, AIUsageScope scope, decimal cost) => new()
        {
            PartitionKey = Key(period.Key),
            RowKey = Key(scope.ToString()),
            Cost = cost.ToString(CultureInfo.InvariantCulture),
            Calls = 1,
        };

        internal void Add(decimal cost)
        {
            Cost = (decimal.Parse(Cost, CultureInfo.InvariantCulture) + cost).ToString(CultureInfo.InvariantCulture);
            Calls++;
        }

        internal AIUsageTotals ToTotals() => new(decimal.Parse(Cost, CultureInfo.InvariantCulture), Calls);
    }

    internal sealed class UsageRow : ITableEntity
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public DateTimeOffset At { get; set; }
        public string Feature { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string Caller { get; set; } = "";
        public long InputTokens { get; set; }
        public long CachedInputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long ReasoningTokens { get; set; }
        public int Images { get; set; }
        public string? Cost { get; set; }
        public bool Complete { get; set; }
        public long DurationTicks { get; set; }
        public string? Error { get; set; }

        internal static UsageRow From(AIUsageRecord record) => new()
        {
            PartitionKey = record.At.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // Newest first within a day, and unique when two calls start in the same tick.
            RowKey = $"{DateTimeOffset.MaxValue.UtcTicks - record.At.UtcTicks:D19}_{Guid.NewGuid():N}",
            At = record.At,
            Feature = record.Feature,
            Provider = record.Provider,
            Model = record.Model,
            Caller = record.Caller,
            InputTokens = record.InputTokens,
            CachedInputTokens = record.CachedInputTokens,
            OutputTokens = record.OutputTokens,
            ReasoningTokens = record.ReasoningTokens,
            Images = record.Images,
            Cost = record.Cost?.ToString(CultureInfo.InvariantCulture),
            Complete = record.Complete,
            DurationTicks = record.Duration.Ticks,
            Error = record.Error is { Length: > 30_000 } error ? error[..30_000] : record.Error,
        };

        internal AIUsageRecord ToRecord() => new(
            At, Feature, Provider, Model, Caller,
            InputTokens, CachedInputTokens, OutputTokens, ReasoningTokens, Images,
            Cost is null ? null : decimal.Parse(Cost, CultureInfo.InvariantCulture),
            Complete, TimeSpan.FromTicks(DurationTicks), Error);
    }
}
