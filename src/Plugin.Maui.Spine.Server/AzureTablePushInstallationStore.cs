using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// The register in Azure Table Storage. Installations are partitioned by platform, and a second
/// table maps tag to installation so a query does not have to read every row.
/// </summary>
/// <param name="connectionString">The storage account; <c>UseDevelopmentStorage=true</c> for Azurite.</param>
/// <param name="prefix">Prepended to both table names, so several apps can share an account.</param>
/// <param name="timeProvider">The clock expiry is measured against.</param>
public sealed class AzureTablePushInstallationStore(
    string connectionString,
    string prefix = "SpinePush",
    TimeProvider? timeProvider = null) : IPushInstallationStore
{
    private readonly TableClient _installations = new(connectionString, $"{prefix}Installations");
    private readonly TableClient _tags = new(connectionString, $"{prefix}Tags");
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _created = new(1, 1);
    private bool _tablesExist;

    /// <inheritdoc />
    public async Task UpsertAsync(PushInstallation installation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        await EnsureTablesAsync(cancellationToken);

        var previous = await ReadAsync(installation.Platform, installation.Id, cancellationToken);

        await _installations.UpsertEntityAsync(Row.From(installation), TableUpdateMode.Replace, cancellationToken);
        await ReindexAsync(installation.Id, previous?.Tags ?? [], installation.Tags, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PushInstallation?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureTablesAsync(cancellationToken);

        // The id alone does not say which partition it is in, so every platform is asked. There are
        // three of them, and a point read is the cheapest thing Table Storage does.
        foreach (var platform in Enum.GetValues<PushPlatform>())
        {
            if (await ReadAsync(platform, id, cancellationToken) is { } found) return found;
        }

        return null;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await GetAsync(id, cancellationToken) is not { } installation) return;

        await _installations.DeleteEntityAsync(installation.Platform.ToString(), id, ETag.All, cancellationToken);
        await ReindexAsync(id, installation.Tags, [], cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PushInstallation> QueryAsync(
        PushTagExpression expression,
        IReadOnlyCollection<PushPlatform>? platforms = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        await EnsureTablesAsync(cancellationToken);

        var now = _time.GetUtcNow();
        var wanted = platforms is { Count: > 0 } ? platforms : Enum.GetValues<PushPlatform>();

        // A tag every match must carry turns the query into one index read instead of a scan. Without
        // one — PushTarget.All, or an expression that is only negations — there is nothing to narrow by.
        if (expression.RequiredTags.FirstOrDefault() is { } indexed)
        {
            await foreach (var id in IndexedIdsAsync(indexed, cancellationToken))
            {
                if (await GetAsync(id, cancellationToken) is not { } installation) continue;
                if (!wanted.Contains(installation.Platform)) continue;
                if (installation.IsExpired(now)) continue;
                if (!expression.Matches(installation.Tags)) continue;

                yield return installation;
            }

            yield break;
        }

        foreach (var platform in wanted)
        {
            var partition = platform.ToString();

            await foreach (var row in _installations
                .QueryAsync<Row>(r => r.PartitionKey == partition, cancellationToken: cancellationToken))
            {
                var installation = row.ToInstallation();
                if (installation.IsExpired(now)) continue;
                if (!expression.Matches(installation.Tags)) continue;

                yield return installation;
            }
        }
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        await EnsureTablesAsync(cancellationToken);

        var now = _time.GetUtcNow();
        var removed = 0;

        await foreach (var row in _installations.QueryAsync<Row>(cancellationToken: cancellationToken))
        {
            var installation = row.ToInstallation();
            if (installation.UpdatedAt >= olderThan && !installation.IsExpired(now)) continue;

            await _installations.DeleteEntityAsync(row.PartitionKey, row.RowKey, ETag.All, cancellationToken);
            await ReindexAsync(installation.Id, installation.Tags, [], cancellationToken);
            removed++;
        }

        return removed;
    }

    /// <inheritdoc />
    public async Task<int> InvalidateAsync(string handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        await EnsureTablesAsync(cancellationToken);

        // There is no index on the handle. It is only read when a transport reports a dead token,
        // which is rare next to sending, so a scan is the right trade rather than a third table.
        var removed = 0;

        await foreach (var row in _installations
            .QueryAsync<Row>(r => r.Handle == handle, cancellationToken: cancellationToken))
        {
            await _installations.DeleteEntityAsync(row.PartitionKey, row.RowKey, ETag.All, cancellationToken);
            await ReindexAsync(row.RowKey, row.ToInstallation().Tags, [], cancellationToken);
            removed++;
        }

        return removed;
    }

    private async Task<PushInstallation?> ReadAsync(PushPlatform platform, string id, CancellationToken cancellationToken)
    {
        try
        {
            var row = await _installations.GetEntityAsync<Row>(platform.ToString(), id, cancellationToken: cancellationToken);
            return row.Value.ToInstallation();
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    private async IAsyncEnumerable<string> IndexedIdsAsync(
        string tag, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in _tags
            .QueryAsync<TableEntity>(r => r.PartitionKey == tag, cancellationToken: cancellationToken))
        {
            yield return row.RowKey;
        }
    }

    /// <summary>Brings the tag index in line with what the installation now carries.</summary>
    private async Task ReindexAsync(
        string id, IReadOnlyList<string> before, IReadOnlyList<string> after, CancellationToken cancellationToken)
    {
        foreach (var tag in before.Except(after, StringComparer.Ordinal))
            await _tags.DeleteEntityAsync(tag, id, ETag.All, cancellationToken);

        foreach (var tag in after.Except(before, StringComparer.Ordinal))
            await _tags.UpsertEntityAsync(new TableEntity(tag, id), TableUpdateMode.Replace, cancellationToken);
    }

    private async Task EnsureTablesAsync(CancellationToken cancellationToken)
    {
        if (_tablesExist) return;

        await _created.WaitAsync(cancellationToken);
        try
        {
            if (_tablesExist) return;
            await _installations.CreateIfNotExistsAsync(cancellationToken);
            await _tags.CreateIfNotExistsAsync(cancellationToken);
            _tablesExist = true;
        }
        finally
        {
            _created.Release();
        }
    }

    /// <summary>
    /// One installation as a table row. Tags and the Live Activity tokens are JSON in a single column:
    /// Table Storage has no lists, and neither is ever filtered on in a query — the tag index is what
    /// filtering goes through.
    /// </summary>
    internal sealed class Row : ITableEntity
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Handle { get; set; } = "";
        public string? Environment { get; set; }
        public string Tags { get; set; } = "[]";
        public string? UserId { get; set; }
        public string? AppVersion { get; set; }
        public string? OsVersion { get; set; }
        public string? LiveActivities { get; set; }
        public string? WidgetToken { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }

        internal static Row From(PushInstallation installation) => new()
        {
            PartitionKey = installation.Platform.ToString(),
            RowKey = installation.Id,
            Handle = installation.Handle,
            Environment = installation.Environment?.ToString(),
            Tags = JsonSerializer.Serialize(installation.Tags),
            UserId = installation.UserId,
            AppVersion = installation.AppVersion,
            OsVersion = installation.OsVersion,
            LiveActivities = installation.LiveActivities is { } tokens ? JsonSerializer.Serialize(tokens) : null,
            WidgetToken = installation.WidgetToken,
            UpdatedAt = installation.UpdatedAt,
            ExpiresAt = installation.ExpiresAt,
        };

        internal PushInstallation ToInstallation() => new()
        {
            Id = RowKey,
            Platform = Enum.Parse<PushPlatform>(PartitionKey),
            Handle = Handle,
            Environment = Environment is null ? null : Enum.Parse<ApnsEnvironment>(Environment),
            Tags = JsonSerializer.Deserialize<string[]>(Tags) ?? [],
            UserId = UserId,
            AppVersion = AppVersion,
            OsVersion = OsVersion,
            LiveActivities = LiveActivities is null ? null : JsonSerializer.Deserialize<LiveActivityTokens>(LiveActivities),
            WidgetToken = WidgetToken,
            UpdatedAt = UpdatedAt,
            ExpiresAt = ExpiresAt,
        };
    }
}
