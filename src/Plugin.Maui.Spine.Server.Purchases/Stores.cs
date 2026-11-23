using System.Collections.Concurrent;
using System.Text;
using Azure;
using Azure.Data.Tables;

namespace Plugin.Maui.Spine.Server.Purchases;

/// <summary>Where each caller's ledger is kept. Entries are written once by id, except <see cref="LedgerEntryKind.Owned"/> rows.</summary>
public interface IPurchaseLedgerStore
{
    /// <summary>Every entry of <paramref name="caller"/>'s ledger.</summary>
    Task<IReadOnlyList<LedgerEntry>> ReadAsync(string caller, CancellationToken cancellationToken);

    /// <summary>Writes <paramref name="entry"/> unless an entry with its id is there. False when it was there already.</summary>
    Task<bool> AddAsync(string caller, LedgerEntry entry, CancellationToken cancellationToken);

    /// <summary>Writes <paramref name="entry"/> over the entry with its id: an owned product's latest state.</summary>
    Task UpsertAsync(string caller, LedgerEntry entry, CancellationToken cancellationToken);
}

/// <summary>Ledgers in memory, for development and tests. Lost when the process ends.</summary>
public sealed class InMemoryPurchaseLedgerStore : IPurchaseLedgerStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, LedgerEntry>> _ledgers = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<IReadOnlyList<LedgerEntry>> ReadAsync(string caller, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(_ledgers.TryGetValue(caller, out var ledger) ? [.. ledger.Values.OrderBy(e => e.At)] : []);

    /// <inheritdoc/>
    public Task<bool> AddAsync(string caller, LedgerEntry entry, CancellationToken cancellationToken) =>
        Task.FromResult(Ledger(caller).TryAdd(entry.Id, entry));

    /// <inheritdoc/>
    public Task UpsertAsync(string caller, LedgerEntry entry, CancellationToken cancellationToken)
    {
        Ledger(caller)[entry.Id] = entry;
        return Task.CompletedTask;
    }

    private ConcurrentDictionary<string, LedgerEntry> Ledger(string caller) =>
        _ledgers.GetOrAdd(caller, static _ => new ConcurrentDictionary<string, LedgerEntry>(StringComparer.Ordinal));
}

/// <summary>
/// Ledgers in Azure Table Storage: one table, <c>{prefix}Ledger</c>, a partition per caller and a row per entry. The
/// table is created on first use. <c>UseDevelopmentStorage=true</c> is Azurite.
/// </summary>
/// <param name="connectionString">The storage account's connection string.</param>
/// <param name="prefix">The table name's prefix.</param>
public sealed class AzureTablePurchaseLedgerStore(string connectionString, string prefix = "SpinePurchases") : IPurchaseLedgerStore
{
    private readonly TableClient _table = new(connectionString, $"{prefix}Ledger");
    private readonly SemaphoreSlim _created = new(1, 1);
    private bool _exists;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LedgerEntry>> ReadAsync(string caller, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken);
        var entries = new List<LedgerEntry>();
        await foreach (var row in _table.QueryAsync<TableEntity>(e => e.PartitionKey == Key(caller), cancellationToken: cancellationToken))
            entries.Add(FromRow(row));
        return [.. entries.OrderBy(e => e.At)];
    }

    /// <inheritdoc/>
    public async Task<bool> AddAsync(string caller, LedgerEntry entry, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken);
        try
        {
            await _table.AddEntityAsync(ToRow(caller, entry), cancellationToken);
            return true;
        }
        catch (RequestFailedException e) when (e.Status == 409)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(string caller, LedgerEntry entry, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken);
        await _table.UpsertEntityAsync(ToRow(caller, entry), TableUpdateMode.Replace, cancellationToken);
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_exists)
            return;
        await _created.WaitAsync(cancellationToken);
        try
        {
            if (!_exists)
            {
                await _table.CreateIfNotExistsAsync(cancellationToken);
                _exists = true;
            }
        }
        finally
        {
            _created.Release();
        }
    }

    private static TableEntity ToRow(string caller, LedgerEntry entry) => new(Key(caller), Key(entry.Id))
    {
        ["Id"] = entry.Id,
        ["Kind"] = entry.Kind.ToString(),
        ["At"] = entry.At,
        ["Credit"] = entry.Credit,
        ["Amount"] = entry.Amount,
        ["Reference"] = entry.Reference,
        ["Product"] = entry.Product,
        ["ExpiresAt"] = entry.ExpiresAt,
        ["WillRenew"] = entry.WillRenew,
        ["Phase"] = entry.Phase.ToString(),
    };

    private static LedgerEntry FromRow(TableEntity row) => new(
        row.GetString("Id"),
        Enum.Parse<LedgerEntryKind>(row.GetString("Kind")),
        row.GetDateTimeOffset("At") ?? DateTimeOffset.MinValue,
        row.GetString("Credit"),
        row.GetInt32("Amount") ?? 0,
        row.GetString("Reference"),
        row.GetString("Product"),
        row.GetDateTimeOffset("ExpiresAt"),
        row.GetBoolean("WillRenew") ?? false,
        Enum.TryParse<PurchasePhase>(row.GetString("Phase"), out var phase) ? phase : PurchasePhase.Regular);

    // Keys may not hold / \ # ? or control characters; callers and references come from outside, so they are base64url.
    private static string Key(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
