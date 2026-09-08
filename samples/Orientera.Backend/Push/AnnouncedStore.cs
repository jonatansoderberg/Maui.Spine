using Azure;
using Azure.Data.Tables;
using Orientera.Domain;

namespace Orientera.Backend.Push;

/// <summary>
/// What the backend has already pushed about, kept in Table Storage next to the register.
/// </summary>
/// <remarks>
/// It has to survive a restart. The alternative — comparing against what happens to be in the
/// response cache — would announce the same results again every time the host is recycled, which is
/// the one mistake a push backend cannot make quietly.
/// </remarks>
/// <param name="connectionString">The storage account; <c>UseDevelopmentStorage=true</c> for Azurite.</param>
/// <param name="table">The table name.</param>
public sealed class AnnouncedStore(string connectionString, string table = "OrienteraAnnounced")
{
    private readonly TableClient _table = new(connectionString, table);
    private readonly SemaphoreSlim _created = new(1, 1);
    private bool _exists;

    /// <summary>Everything already announced for <paramref name="kind"/>.</summary>
    public async Task<IReadOnlySet<CompetitionId>> ReadAsync(string kind, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);

        var announced = new HashSet<CompetitionId>();

        await foreach (var row in _table.QueryAsync<TableEntity>(
            r => r.PartitionKey == kind, cancellationToken: cancellationToken))
        {
            announced.Add(new CompetitionId(row.RowKey));
        }

        return announced;
    }

    /// <summary>Records that <paramref name="competition"/> has been announced.</summary>
    public async Task MarkAsync(
        string kind, CompetitionId competition, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);

        await _table.UpsertEntityAsync(
            new TableEntity(kind, competition.Value) { ["AnnouncedAt"] = at },
            TableUpdateMode.Replace,
            cancellationToken);
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_exists) return;

        await _created.WaitAsync(cancellationToken);
        try
        {
            if (_exists) return;
            await _table.CreateIfNotExistsAsync(cancellationToken);
            _exists = true;
        }
        finally
        {
            _created.Release();
        }
    }
}
