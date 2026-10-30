using System.Net.Sockets;
using Azure.Data.Tables;
using Xunit;

namespace Plugin.Maui.Spine.Server.Authentication.Tests;

/// <summary>A fact that runs only when a storage emulator answers on 127.0.0.1:10002.</summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    public AzuriteFactAttribute()
    {
        if (!AzureTableStoreTests.EmulatorIsUp)
            Skip = "No storage emulator on 127.0.0.1:10002. Start Azurite (azurite-table) or an Aspire AppHost with storage.";
    }
}

/// <summary>
/// Uniqueness of emails, single use of refresh tokens and one-time values, and key escaping are Table Storage
/// behaviour, so these run against Azurite.
/// </summary>
public sealed class AzureTableStoreTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("SPINE_TEST_STORAGE") ?? "UseDevelopmentStorage=true";

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly string _prefix = $"SpineAuthTest{Guid.NewGuid():N}"[..26];
    private AzureTableUserResolver _users = null!;
    private AzureTableSpineAuthStore _store = null!;

    internal static bool EmulatorIsUp { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            using var socket = new TcpClient();
            return socket.ConnectAsync("127.0.0.1", 10002).Wait(TimeSpan.FromSeconds(2)) && socket.Connected;
        }
        catch
        {
            return false;
        }
    }

    public Task InitializeAsync()
    {
        _users = new AzureTableUserResolver(ConnectionString, _prefix);
        _store = new AzureTableSpineAuthStore(ConnectionString, _prefix);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (!EmulatorIsUp) return;
        foreach (var table in new[] { "Users", "Emails", "Logins", "UserLogins", "Refresh", "Families", "OneTime" })
            await new TableClient(ConnectionString, $"{_prefix}{table}").DeleteAsync();
    }

    private static SpineAuthUser User(string id, string? email) => new() { Id = id, Email = email, CreatedAt = Now };

    [AzuriteFact]
    public async Task A_family_knows_its_newest_token_and_a_retired_one_stays_retired()
    {
        await _store.AddRefreshAsync(new SpineRefreshToken("h1", "f1", "u1", "apple", Now.AddDays(60)));
        Assert.True(await _store.TryUseRefreshAsync("h1", Now));
        await _store.AddRefreshAsync(new SpineRefreshToken("h2", "f1", "u1", "apple", Now.AddDays(60).AddMinutes(1)) { IssuedFrom = "h1" });

        var latest = await _store.FindLatestRefreshAsync("u1", "f1");
        Assert.Equal("h2", latest!.Hash);
        Assert.Equal("h1", latest.IssuedFrom);
        Assert.Null(latest.SupersededAt);
        Assert.Null((await _store.FindRefreshAsync("h1"))!.IssuedFrom);

        await _store.SupersedeRefreshAsync("h2", Now.AddHours(3));
        Assert.Equal(Now.AddHours(3), (await _store.FindRefreshAsync("h2"))!.SupersededAt);
        Assert.Null(await _store.FindLatestRefreshAsync("u1", "no-such-family"));
        await _store.SupersedeRefreshAsync("no-such-token", Now);
        Assert.Null(await _store.FindRefreshAsync("no-such-token"));
    }

    [AzuriteFact]
    public async Task An_email_belongs_to_one_user_whatever_its_case()
    {
        Assert.True(await _users.CreateAsync(User("u1", "Anna@Example.com")));
        Assert.False(await _users.CreateAsync(User("u2", "anna@example.COM")));

        Assert.Equal("u1", (await _users.FindByEmailAsync("ANNA@example.com"))!.Id);
        Assert.Null(await _users.FindByIdAsync("u2"));
    }

    [AzuriteFact]
    public async Task Logins_find_their_user_and_go_with_it()
    {
        await _users.CreateAsync(User("u1", "anna@example.com"));
        await _users.SaveLoginAsync(new SpineAuthLogin("apple", "001234.abc/def#?", "u1") { ProviderRefreshToken = "r", ProviderClientId = "se.app" });
        await _users.SaveLoginAsync(new SpineAuthLogin("password", "u1", "u1"));

        Assert.Equal("u1", (await _users.FindByLoginAsync("apple", "001234.abc/def#?"))!.Id);
        var logins = await _users.GetLoginsAsync("u1");
        Assert.Equal(2, logins.Count);
        Assert.Equal("r", logins.Single(l => l.Provider == "apple").ProviderRefreshToken);

        await _users.DeleteAsync("u1");

        Assert.Null(await _users.FindByIdAsync("u1"));
        Assert.Null(await _users.FindByLoginAsync("apple", "001234.abc/def#?"));
        Assert.True(await _users.CreateAsync(User("u3", "anna@example.com")));
    }

    [AzuriteFact]
    public async Task A_login_keeps_its_details_and_can_be_removed_alone()
    {
        await _users.CreateAsync(User("u1", "anna@example.com"));
        await _users.SaveLoginAsync(new SpineAuthLogin("google", "g-1", "u1") { Email = "bo@example.com", DisplayName = "Bo", LinkedAt = Now });
        await _users.SaveLoginAsync(new SpineAuthLogin("apple", "a-1", "u1"));

        var google = (await _users.GetLoginsAsync("u1")).Single(l => l.Provider == "google");
        Assert.Equal(("bo@example.com", "Bo", Now), (google.Email, google.DisplayName, google.LinkedAt));

        await _users.DeleteLoginAsync(google);

        Assert.Null(await _users.FindByLoginAsync("google", "g-1"));
        Assert.Equal("apple", Assert.Single(await _users.GetLoginsAsync("u1")).Provider);
        Assert.Equal("u1", (await _users.FindByIdAsync("u1"))!.Id);
    }

    [AzuriteFact]
    public async Task A_new_email_moves_the_index_unverified_and_never_takes_another_users()
    {
        var anna = User("u1", "relay@privaterelay.appleid.com");
        anna.EmailVerified = true;
        await _users.CreateAsync(anna);
        await _users.CreateAsync(User("u2", "bo@example.com"));

        Assert.False(await _users.ChangeEmailAsync("u1", "BO@example.com"));
        Assert.True(await _users.ChangeEmailAsync("u1", "anna@home.example"));

        var changed = (await _users.FindByIdAsync("u1"))!;
        Assert.Equal(("anna@home.example", false), (changed.Email, changed.EmailVerified));
        Assert.Equal("u1", (await _users.FindByEmailAsync("Anna@Home.example"))!.Id);
        Assert.Null(await _users.FindByEmailAsync("relay@privaterelay.appleid.com"));
        Assert.Equal("u2", (await _users.FindByEmailAsync("bo@example.com"))!.Id);
        Assert.True(await _users.CreateAsync(User("u3", "relay@privaterelay.appleid.com")));
    }

    [AzuriteFact]
    public async Task An_update_keeps_the_email_and_saves_the_rest()
    {
        await _users.CreateAsync(User("u1", "anna@example.com"));
        var user = (await _users.FindByIdAsync("u1"))!;
        user.Email = "other@example.com";
        user.PasswordHash = "hash";
        user.FailedSignIns = 3;
        user.LockedUntil = Now.AddMinutes(15);
        await _users.UpdateAsync(user);

        var saved = (await _users.FindByIdAsync("u1"))!;
        Assert.Equal("anna@example.com", saved.Email);
        Assert.Equal("hash", saved.PasswordHash);
        Assert.Equal(3, saved.FailedSignIns);
        Assert.Equal(Now.AddMinutes(15), saved.LockedUntil);
    }

    [AzuriteFact]
    public async Task An_update_from_a_stale_read_is_refused()
    {
        await _users.CreateAsync(User("u1", "anna@example.com"));
        var first = (await _users.FindByIdAsync("u1"))!;
        var second = (await _users.FindByIdAsync("u1"))!;

        first.FailedSignIns = 1;
        Assert.True(await _users.UpdateAsync(first));

        second.PasswordHash = "stale";
        Assert.False(await _users.UpdateAsync(second));
        Assert.Null((await _users.FindByIdAsync("u1"))!.PasswordHash);
    }

    [AzuriteFact]
    public async Task A_one_time_value_is_taken_once_and_not_after_it_expires()
    {
        await _store.SaveOneTimeAsync("k1", "v", Now.AddMinutes(5));
        await _store.SaveOneTimeAsync("k2", "v", Now.AddMinutes(-1));

        var takes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _store.TakeOneTimeAsync("k1", Now)));
        Assert.Equal(1, takes.Count(t => t == "v"));
        Assert.Null(await _store.TakeOneTimeAsync("k2", Now));
    }

    [AzuriteFact]
    public async Task A_refresh_token_is_used_once_even_when_raced()
    {
        await _store.AddRefreshAsync(new SpineRefreshToken("h1", "f1", "u1", "apple", Now.AddDays(60)));

        var uses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _store.TryUseRefreshAsync("h1", Now)));
        Assert.Equal(1, uses.Count(u => u));
        Assert.Equal(Now, (await _store.FindRefreshAsync("h1"))!.UsedAt);
    }

    [AzuriteFact]
    public async Task Families_are_revoked_one_at_a_time_or_all_together()
    {
        await _store.AddRefreshAsync(new SpineRefreshToken("h1", "f1", "u1", "apple", Now.AddDays(60)));
        await _store.AddRefreshAsync(new SpineRefreshToken("h2", "f2", "u1", "apple", Now.AddDays(60)));
        await _store.AddRefreshAsync(new SpineRefreshToken("h3", "f3", "u2", "apple", Now.AddDays(60)));

        await _store.RevokeFamilyAsync("u1", "f1");
        Assert.True(await _store.IsFamilyRevokedAsync("u1", "f1"));
        Assert.False(await _store.IsFamilyRevokedAsync("u1", "f2"));

        await _store.RevokeUserAsync("u1");
        Assert.True(await _store.IsFamilyRevokedAsync("u1", "f2"));
        Assert.False(await _store.IsFamilyRevokedAsync("u2", "f3"));
        Assert.True(await _store.IsFamilyRevokedAsync("u9", "unknown"));
    }

    [AzuriteFact]
    public async Task A_deleted_users_tokens_and_families_are_removed()
    {
        await _store.AddRefreshAsync(new SpineRefreshToken("h1", "f1", "u1", "apple", Now.AddDays(60)));
        await _store.AddRefreshAsync(new SpineRefreshToken("h2", "f2", "u1", "apple", Now.AddDays(60)));
        await _store.AddRefreshAsync(new SpineRefreshToken("h3", "f3", "u2", "apple", Now.AddDays(60)));

        await _store.ForgetUserAsync("u1");

        Assert.Null(await _store.FindRefreshAsync("h1"));
        Assert.Null(await _store.FindRefreshAsync("h2"));
        Assert.NotNull(await _store.FindRefreshAsync("h3"));
        Assert.True(await _store.IsFamilyRevokedAsync("u1", "f1"));
        Assert.Equal(["f3"], await FamiliesAsync());
    }

    [AzuriteFact]
    public async Task Prune_removes_revoked_and_spent_families_and_keeps_live_ones()
    {
        await _store.AddRefreshAsync(new SpineRefreshToken("h1", "revoked", "u1", "apple", Now.AddDays(60)));
        await _store.RevokeFamilyAsync("u1", "revoked");
        await _store.AddRefreshAsync(new SpineRefreshToken("h2", "spent", "u1", "apple", Now.AddDays(-1)));
        await _store.AddRefreshAsync(new SpineRefreshToken("h3", "live", "u1", "apple", Now.AddDays(-2)));
        await _store.AddRefreshAsync(new SpineRefreshToken("h4", "live", "u1", "apple", Now.AddDays(30)));

        // Families written before they had ExpiresAt: one with a token left, one with none.
        var families = new TableClient(ConnectionString, $"{_prefix}Families");
        await _store.AddRefreshAsync(new SpineRefreshToken("h5", "old-live", "u2", "apple", Now.AddDays(30)));
        await families.UpsertEntityAsync(new TableEntity(TableKey.Encode("u2"), "old-live") { ["Revoked"] = false }, TableUpdateMode.Replace);
        await families.UpsertEntityAsync(new TableEntity(TableKey.Encode("u2"), "old-spent") { ["Revoked"] = false }, TableUpdateMode.Replace);

        // h2 and h3, then the revoked, spent and old-spent families.
        Assert.Equal(5, await _store.PruneAsync(Now));

        Assert.Equal(["live", "old-live"], await FamiliesAsync());
        Assert.False(await _store.IsFamilyRevokedAsync("u1", "live"));
        Assert.NotNull(await _store.FindRefreshAsync("h1"));
    }

    private async Task<string[]> FamiliesAsync()
    {
        var rows = new List<string>();
        await foreach (var row in new TableClient(ConnectionString, $"{_prefix}Families").QueryAsync<TableEntity>()) rows.Add(row.RowKey);
        return [.. rows.Order()];
    }

    [AzuriteFact]
    public async Task Prune_removes_what_expired()
    {
        await _store.AddRefreshAsync(new SpineRefreshToken("old", "f1", "u1", "apple", Now.AddDays(-1)));
        await _store.AddRefreshAsync(new SpineRefreshToken("new", "f1", "u1", "apple", Now.AddDays(1)));
        await _store.SaveOneTimeAsync("gone", "v", Now.AddMinutes(-1));

        Assert.Equal(2, await _store.PruneAsync(Now));
        Assert.Null(await _store.FindRefreshAsync("old"));
        Assert.NotNull(await _store.FindRefreshAsync("new"));
    }
}
