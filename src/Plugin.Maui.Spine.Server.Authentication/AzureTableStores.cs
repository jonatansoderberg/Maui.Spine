using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.IdentityModel.Tokens;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>
/// Users in Azure Table Storage: <c>{prefix}Users</c> by id, <c>{prefix}Emails</c> as the unique email index, and
/// <c>{prefix}Logins</c> twice, by provider and subject and by user.
/// </summary>
/// <param name="connectionString">The storage account; <c>UseDevelopmentStorage=true</c> for Azurite.</param>
/// <param name="prefix">Prepended to every table name.</param>
public sealed class AzureTableUserResolver(string connectionString, string prefix = "SpineAuth") : IUserResolver
{
    private const string Row = "";

    private readonly TableClient _users = new(connectionString, $"{prefix}Users");
    private readonly TableClient _emails = new(connectionString, $"{prefix}Emails");
    private readonly TableClient _logins = new(connectionString, $"{prefix}Logins");
    private readonly TableClient _userLogins = new(connectionString, $"{prefix}UserLogins");
    private readonly TableTables _tables = new();

    /// <inheritdoc />
    public async Task<SpineAuthUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);
        await EnsureAsync(cancellationToken);

        var row = await _users.GetEntityIfExistsAsync<TableEntity>(TableKey.Encode(userId), Row, cancellationToken: cancellationToken);
        return row.HasValue ? ToUser(row.Value!) : null;
    }

    /// <inheritdoc />
    public async Task<SpineAuthUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        await EnsureAsync(cancellationToken);

        var index = await _emails.GetEntityIfExistsAsync<TableEntity>(EmailKey(email), Row, cancellationToken: cancellationToken);
        return index.HasValue && index.Value!.GetString("UserId") is { } id ? await FindByIdAsync(id, cancellationToken) : null;
    }

    /// <inheritdoc />
    public async Task<SpineAuthUser?> FindByLoginAsync(string provider, string subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(subject);
        await EnsureAsync(cancellationToken);

        var login = await _logins.GetEntityIfExistsAsync<TableEntity>(LoginKey(provider, subject), Row, cancellationToken: cancellationToken);
        return login.HasValue && login.Value!.GetString("UserId") is { } id ? await FindByIdAsync(id, cancellationToken) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpineAuthLogin>> GetLoginsAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);
        await EnsureAsync(cancellationToken);

        var logins = new List<SpineAuthLogin>();
        await foreach (var row in _userLogins.QueryAsync<TableEntity>(r => r.PartitionKey == TableKey.Encode(userId), cancellationToken: cancellationToken))
            logins.Add(ToLogin(row));

        return logins;
    }

    /// <inheritdoc />
    public async Task<bool> CreateAsync(SpineAuthUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        await EnsureAsync(cancellationToken);

        // The index row goes first: its insert is what makes an email unique when two sign-ups race.
        if (user.Email is { } email)
        {
            try
            {
                await _emails.AddEntityAsync(new TableEntity(EmailKey(email), Row) { ["UserId"] = user.Id }, cancellationToken);
            }
            catch (RequestFailedException e) when (e.Status == 409)
            {
                return false;
            }
        }

        await _users.UpsertEntityAsync(ToRow(user), TableUpdateMode.Replace, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(SpineAuthUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        await EnsureAsync(cancellationToken);

        if (user.Version is null || await FindByIdAsync(user.Id, cancellationToken) is not { } current) return false;

        var row = ToRow(user);
        row["Email"] = current.Email;

        try
        {
            await _users.UpdateEntityAsync(row, new ETag(user.Version), TableUpdateMode.Replace, cancellationToken);
            return true;
        }
        catch (RequestFailedException e) when (e.Status is 404 or 412)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task SaveLoginAsync(SpineAuthLogin login, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);
        await EnsureAsync(cancellationToken);

        var key = LoginKey(login.Provider, login.Subject);
        await _logins.UpsertEntityAsync(LoginRow(key, Row, login), TableUpdateMode.Replace, cancellationToken);
        await _userLogins.UpsertEntityAsync(LoginRow(TableKey.Encode(login.UserId), key, login), TableUpdateMode.Replace, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteLoginAsync(SpineAuthLogin login, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);
        await EnsureAsync(cancellationToken);

        var key = LoginKey(login.Provider, login.Subject);
        await _logins.DeleteEntityAsync(key, Row, ETag.All, cancellationToken);
        await _userLogins.DeleteEntityAsync(TableKey.Encode(login.UserId), key, ETag.All, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);
        await EnsureAsync(cancellationToken);

        foreach (var login in await GetLoginsAsync(userId, cancellationToken))
        {
            var key = LoginKey(login.Provider, login.Subject);
            await _logins.DeleteEntityAsync(key, Row, ETag.All, cancellationToken);
            await _userLogins.DeleteEntityAsync(TableKey.Encode(userId), key, ETag.All, cancellationToken);
        }

        if (await FindByIdAsync(userId, cancellationToken) is { Email: { } email })
            await _emails.DeleteEntityAsync(EmailKey(email), Row, ETag.All, cancellationToken);

        await _users.DeleteEntityAsync(TableKey.Encode(userId), Row, ETag.All, cancellationToken);
    }

    private Task EnsureAsync(CancellationToken cancellationToken) => _tables.EnsureAsync([_users, _emails, _logins, _userLogins], cancellationToken);

    private static string EmailKey(string email) => TableKey.Encode(email.Trim().ToUpperInvariant());

    private static string LoginKey(string provider, string subject) => TableKey.Encode($"{provider.ToLowerInvariant()}|{subject}");

    private static TableEntity ToRow(SpineAuthUser user) => new(TableKey.Encode(user.Id), Row)
    {
        ["Id"] = user.Id,
        ["Email"] = user.Email,
        ["EmailVerified"] = user.EmailVerified,
        ["DisplayName"] = user.DisplayName,
        ["PictureUrl"] = user.PictureUrl,
        ["GivenName"] = user.GivenName,
        ["FamilyName"] = user.FamilyName,
        ["Organization"] = user.Organization,
        ["PasswordHash"] = user.PasswordHash,
        ["FailedSignIns"] = user.FailedSignIns,
        ["LockedUntil"] = user.LockedUntil,
        ["CreatedAt"] = user.CreatedAt,
    };

    private static SpineAuthUser ToUser(TableEntity row) => new()
    {
        Id = row.GetString("Id"),
        Email = row.GetString("Email"),
        EmailVerified = row.GetBoolean("EmailVerified") ?? false,
        DisplayName = row.GetString("DisplayName"),
        PictureUrl = row.GetString("PictureUrl"),
        GivenName = row.GetString("GivenName"),
        FamilyName = row.GetString("FamilyName"),
        Organization = row.GetString("Organization"),
        PasswordHash = row.GetString("PasswordHash"),
        FailedSignIns = row.GetInt32("FailedSignIns") ?? 0,
        LockedUntil = row.GetDateTimeOffset("LockedUntil"),
        CreatedAt = row.GetDateTimeOffset("CreatedAt") ?? default,
        Version = row.ETag.ToString(),
    };

    private static TableEntity LoginRow(string partition, string rowKey, SpineAuthLogin login) => new(partition, rowKey)
    {
        ["Provider"] = login.Provider,
        ["Subject"] = login.Subject,
        ["UserId"] = login.UserId,
        ["ProviderRefreshToken"] = login.ProviderRefreshToken,
        ["ProviderClientId"] = login.ProviderClientId,
        ["Email"] = login.Email,
        ["DisplayName"] = login.DisplayName,
        ["LinkedAt"] = login.LinkedAt,
        ["PasskeyPublicKey"] = login.PasskeyPublicKey,
        ["PasskeySignCount"] = login.PasskeyPublicKey is null ? null : (long)login.PasskeySignCount,
    };

    private static SpineAuthLogin ToLogin(TableEntity row) =>
        new(row.GetString("Provider"), row.GetString("Subject"), row.GetString("UserId"))
        {
            ProviderRefreshToken = row.GetString("ProviderRefreshToken"),
            ProviderClientId = row.GetString("ProviderClientId"),
            Email = row.GetString("Email"),
            DisplayName = row.GetString("DisplayName"),
            LinkedAt = row.GetDateTimeOffset("LinkedAt"),
            PasskeyPublicKey = row.GetBinary("PasskeyPublicKey"),
            PasskeySignCount = (uint)(row.GetInt64("PasskeySignCount") ?? 0),
        };
}

/// <summary>
/// Tokens in Azure Table Storage: <c>{prefix}Refresh</c> by hash, <c>{prefix}Families</c> by user, and
/// <c>{prefix}OneTime</c> for nonces and emailed codes. Run <see cref="PruneAsync"/> now and then.
/// </summary>
/// <param name="connectionString">The storage account; <c>UseDevelopmentStorage=true</c> for Azurite.</param>
/// <param name="prefix">Prepended to every table name.</param>
public sealed class AzureTableSpineAuthStore(string connectionString, string prefix = "SpineAuth") : ISpineAuthStore
{
    private const string Row = "";

    private readonly TableClient _refresh = new(connectionString, $"{prefix}Refresh");
    private readonly TableClient _families = new(connectionString, $"{prefix}Families");
    private readonly TableClient _oneTime = new(connectionString, $"{prefix}OneTime");
    private readonly TableTables _tables = new();

    /// <inheritdoc />
    public async Task SaveOneTimeAsync(string key, string value, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);
        await _oneTime.UpsertEntityAsync(new TableEntity(TableKey.Encode(key), Row) { ["Value"] = value, ["ExpiresAt"] = expiresAt },
            TableUpdateMode.Replace, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> TakeOneTimeAsync(string key, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);

        var found = await _oneTime.GetEntityIfExistsAsync<TableEntity>(TableKey.Encode(key), Row, cancellationToken: cancellationToken);
        if (!found.HasValue || found.Value!.GetBoolean("Taken") == true) return null;

        // An update conditional on the row read picks one winner among callers that read it. A conditional delete
        // does not: the SDK reports a delete of a row that is already gone as a success.
        var row = found.Value;
        row["Taken"] = true;

        try
        {
            await _oneTime.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Merge, cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status is 404 or 412)
        {
            return null;
        }

        await _oneTime.DeleteEntityAsync(row.PartitionKey, Row, ETag.All, cancellationToken);
        return row.GetDateTimeOffset("ExpiresAt") > now ? row.GetString("Value") : null;
    }

    /// <inheritdoc />
    public async Task AddRefreshAsync(SpineRefreshToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        await EnsureAsync(cancellationToken);

        await _refresh.AddEntityAsync(new TableEntity(TableKey.Encode(token.Hash), Row)
        {
            ["FamilyId"] = token.FamilyId,
            ["UserId"] = token.UserId,
            ["Provider"] = token.Provider,
            ["ExpiresAt"] = token.ExpiresAt,
        }, cancellationToken);

        try
        {
            await _families.AddEntityAsync(new TableEntity(TableKey.Encode(token.UserId), token.FamilyId) { ["Revoked"] = false }, cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status == 409)
        {
        }
    }

    /// <inheritdoc />
    public async Task<SpineRefreshToken?> FindRefreshAsync(string hash, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);

        var found = await _refresh.GetEntityIfExistsAsync<TableEntity>(TableKey.Encode(hash), Row, cancellationToken: cancellationToken);
        if (!found.HasValue) return null;

        var row = found.Value!;
        return new SpineRefreshToken(hash, row.GetString("FamilyId"), row.GetString("UserId"), row.GetString("Provider"),
            row.GetDateTimeOffset("ExpiresAt") ?? default, row.GetDateTimeOffset("UsedAt"));
    }

    /// <inheritdoc />
    public async Task<bool> TryUseRefreshAsync(string hash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);

        var found = await _refresh.GetEntityIfExistsAsync<TableEntity>(TableKey.Encode(hash), Row, cancellationToken: cancellationToken);
        if (!found.HasValue || found.Value!.GetDateTimeOffset("UsedAt") is not null) return false;

        var row = found.Value;
        row["UsedAt"] = now;

        try
        {
            await _refresh.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, cancellationToken);
            return true;
        }
        catch (RequestFailedException e) when (e.Status is 404 or 412)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsFamilyRevokedAsync(string userId, string familyId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);

        var family = await _families.GetEntityIfExistsAsync<TableEntity>(TableKey.Encode(userId), familyId, cancellationToken: cancellationToken);
        return !family.HasValue || family.Value!.GetBoolean("Revoked") != false;
    }

    /// <inheritdoc />
    public async Task RevokeFamilyAsync(string userId, string familyId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);
        await _families.UpsertEntityAsync(new TableEntity(TableKey.Encode(userId), familyId) { ["Revoked"] = true }, TableUpdateMode.Merge, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);

        var partition = TableKey.Encode(userId);
        await foreach (var family in _families.QueryAsync<TableEntity>(r => r.PartitionKey == partition, cancellationToken: cancellationToken))
            await _families.UpsertEntityAsync(new TableEntity(partition, family.RowKey) { ["Revoked"] = true }, TableUpdateMode.Merge, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);

        var removed = 0;
        foreach (var table in new[] { _refresh, _oneTime })
        {
            var expired = TableClient.CreateQueryFilter($"ExpiresAt le {now}");
            await foreach (var row in table.QueryAsync<TableEntity>(expired, select: ["PartitionKey", "RowKey"], cancellationToken: cancellationToken))
            {
                try
                {
                    await table.DeleteEntityAsync(row.PartitionKey, row.RowKey, ETag.All, cancellationToken);
                    removed++;
                }
                catch (RequestFailedException e) when (e.Status == 404)
                {
                }
            }
        }

        return removed;
    }

    private Task EnsureAsync(CancellationToken cancellationToken) => _tables.EnsureAsync([_refresh, _families, _oneTime], cancellationToken);
}

/// <summary>Table keys may not hold <c>/ \ # ?</c> or control characters, so text from outside goes in as base64url.</summary>
internal static class TableKey
{
    public static string Encode(string value) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(value));
}

/// <summary>Creates a store's tables once, on first use.</summary>
internal sealed class TableTables
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _exist;

    public async Task EnsureAsync(TableClient[] tables, CancellationToken cancellationToken)
    {
        if (_exist) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_exist) return;
            foreach (var table in tables) await table.CreateIfNotExistsAsync(cancellationToken);
            _exist = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
