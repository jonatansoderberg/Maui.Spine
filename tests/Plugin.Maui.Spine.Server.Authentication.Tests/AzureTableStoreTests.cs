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
