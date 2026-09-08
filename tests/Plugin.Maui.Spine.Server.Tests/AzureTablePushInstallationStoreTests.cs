using System.Net.Sockets;
using Azure.Data.Tables;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Common;
using Xunit;
using Xunit.Sdk;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>
/// A fact that runs only when a storage emulator answers. The register is the one piece of the server
/// that cannot be verified against a fake — the tag index, the partitioning and the row round-trip are
/// all Table Storage behaviour — so these run against Azurite (the one the Aspire AppHost starts) and
/// skip when it is not up, rather than being left unwritten or failing on a machine without it.
/// </summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    public AzuriteFactAttribute()
    {
        if (!AzureTablePushInstallationStoreTests.EmulatorIsUp)
            Skip = $"No storage emulator on {AzureTablePushInstallationStoreTests.TableEndpoint}. Start the Aspire AppHost (samples/Orientera.AppHost) or Azurite.";
    }
}

[Collection(nameof(AzureTablePushInstallationStoreTests))]
public class AzureTablePushInstallationStoreTests : IAsyncLifetime
{
    internal const string TableEndpoint = "127.0.0.1:10002";

    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("SPINE_TEST_STORAGE") ?? "UseDevelopmentStorage=true";

    internal static bool EmulatorIsUp { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            var (host, port) = TableEndpoint.Split(':') is [var h, var p] ? (h, int.Parse(p)) : ("127.0.0.1", 10002);
            using var socket = new TcpClient();
            return socket.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(2)) && socket.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static PushInstallation Installation(
        string id, PushPlatform platform = PushPlatform.Apple, string? handle = null,
        DateTimeOffset? updatedAt = null, DateTimeOffset? expiresAt = null, params string[] tags) => new()
    {
        Id = id,
        Platform = platform,
        Handle = handle ?? $"handle-{id}",
        Tags = tags,
        UpdatedAt = updatedAt ?? Now,
        ExpiresAt = expiresAt,
    };

    private readonly List<string> _prefixes = [];

    // Every test gets its own pair of tables, so a leftover row from an earlier run cannot make one pass
    // or fail for the wrong reason, and the tests can run in any order.
    private (AzureTablePushInstallationStore Store, FakeTimeProvider Time) NewStore()
    {
        var time = new FakeTimeProvider(Now);
        var prefix = $"SpineTest{Guid.NewGuid():N}";
        _prefixes.Add(prefix);
        return (new AzureTablePushInstallationStore(ConnectionString, prefix, time), time);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        var service = new TableServiceClient(ConnectionString);
        foreach (var prefix in _prefixes)
        {
            await service.DeleteTableAsync($"{prefix}Installations");
            await service.DeleteTableAsync($"{prefix}Tags");
        }
    }

    private static async Task<List<PushInstallation>> QueryAsync(
        IPushInstallationStore store, string expression, IReadOnlyCollection<PushPlatform>? platforms = null)
    {
        var found = new List<PushInstallation>();
        await foreach (var i in store.QueryAsync(PushTagExpression.Parse(expression), platforms))
            found.Add(i);
        return found;
    }

    [AzuriteFact]
    public async Task Upsert_round_trips_every_field()
    {
        var (store, _) = NewStore();
        var installation = new PushInstallation
        {
            Id = "a",
            Platform = PushPlatform.Apple,
            Handle = "token",
            Environment = ApnsEnvironment.Sandbox,
            Tags = ["kind:pm", "competition:1"],
            UserId = "user-1",
            AppVersion = "1.2.3",
            OsVersion = "iOS 26.0",
            LiveActivities = new LiveActivityTokens
            {
                PushToStart = "start",
                Activities = new Dictionary<string, string> { ["act-1"] = "update" },
            },
            WidgetToken = "widget",
            UpdatedAt = Now,
            ExpiresAt = Now.AddDays(1),
        };

        await store.UpsertAsync(installation);
        var read = await store.GetAsync("a");

        Assert.NotNull(read);
        Assert.Equal(installation.Platform, read.Platform);
        Assert.Equal(installation.Handle, read.Handle);
        Assert.Equal(installation.Environment, read.Environment);
        Assert.Equal(installation.Tags, read.Tags);
        Assert.Equal(installation.UserId, read.UserId);
        Assert.Equal(installation.AppVersion, read.AppVersion);
        Assert.Equal(installation.OsVersion, read.OsVersion);
        Assert.Equal("start", read.LiveActivities?.PushToStart);
        Assert.Equal("update", read.LiveActivities?.Activities["act-1"]);
        Assert.Equal(installation.WidgetToken, read.WidgetToken);
        Assert.Equal(installation.UpdatedAt, read.UpdatedAt);
        Assert.Equal(installation.ExpiresAt, read.ExpiresAt);
    }

    [AzuriteFact]
    public async Task Upsert_replaces_the_registration_with_the_same_id()
    {
        var (store, _) = NewStore();

        await store.UpsertAsync(Installation("a", handle: "first"));
        await store.UpsertAsync(Installation("a", handle: "second"));

        Assert.Equal("second", (await store.GetAsync("a"))!.Handle);
    }

    [AzuriteFact]
    public async Task Get_returns_null_for_an_unknown_id_and_delete_is_forgiving()
    {
        var (store, _) = NewStore();

        Assert.Null(await store.GetAsync("nope"));
        await store.DeleteAsync("nope");
    }

    [AzuriteFact]
    public async Task Query_returns_the_matching_registrations()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("a", tags: ["kind:pm", "competition:1"]));
        await store.UpsertAsync(Installation("b", tags: ["kind:pm", "competition:2"]));
        await store.UpsertAsync(Installation("c", tags: ["kind:results", "competition:1"]));

        var found = await QueryAsync(store, "kind:pm && competition:1");

        Assert.Equal(["a"], found.Select(i => i.Id));
    }

    [AzuriteFact]
    public async Task Query_reaches_the_same_rows_through_the_index_and_through_the_scan()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("a", tags: ["kind:pm", "muted"]));
        await store.UpsertAsync(Installation("b", tags: ["kind:pm"]));

        // "kind:pm && !muted" narrows through the tag index; "!muted" alone has no required tag and scans.
        Assert.Equal(["b"], (await QueryAsync(store, "kind:pm && !muted")).Select(i => i.Id));
        Assert.Equal(["b"], (await QueryAsync(store, "!muted")).Select(i => i.Id));
    }

    [AzuriteFact]
    public async Task Query_does_not_return_a_tag_the_installation_no_longer_carries()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("a", tags: ["kind:pm"]));
        await store.UpsertAsync(Installation("a", tags: ["kind:results"]));

        Assert.Empty(await QueryAsync(store, "kind:pm"));
        Assert.Equal(["a"], (await QueryAsync(store, "kind:results")).Select(i => i.Id));
    }

    [AzuriteFact]
    public async Task Query_can_be_narrowed_to_platforms()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("apple", PushPlatform.Apple, tags: ["t"]));
        await store.UpsertAsync(Installation("android", PushPlatform.Android, tags: ["t"]));

        Assert.Equal(["android"], (await QueryAsync(store, "t", [PushPlatform.Android])).Select(i => i.Id));
    }

    [AzuriteFact]
    public async Task Query_skips_expired_registrations()
    {
        var (store, time) = NewStore();
        await store.UpsertAsync(Installation("live", tags: ["t"]));
        await store.UpsertAsync(Installation("dead", expiresAt: Now.AddHours(1), tags: ["t"]));

        Assert.Equal(2, (await QueryAsync(store, "t")).Count);

        time.SetUtcNow(Now.AddHours(2));

        Assert.Equal(["live"], (await QueryAsync(store, "t")).Select(i => i.Id));
    }

    [AzuriteFact]
    public async Task Match_all_reaches_every_live_registration()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("a"));
        await store.UpsertAsync(Installation("b", PushPlatform.Android, tags: ["t"]));

        var found = new List<PushInstallation>();
        await foreach (var i in store.QueryAsync(PushTagExpression.MatchAll)) found.Add(i);

        Assert.Equal(2, found.Count);
    }

    [AzuriteFact]
    public async Task Delete_removes_the_row_and_its_index_entries()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("a", tags: ["t"]));

        await store.DeleteAsync("a");

        Assert.Null(await store.GetAsync("a"));
        Assert.Empty(await QueryAsync(store, "t"));
    }

    [AzuriteFact]
    public async Task Prune_removes_the_stale_and_the_expired()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("fresh", updatedAt: Now, tags: ["t"]));
        await store.UpsertAsync(Installation("stale", updatedAt: Now.AddDays(-90), tags: ["t"]));
        await store.UpsertAsync(Installation("expired", updatedAt: Now, expiresAt: Now.AddMinutes(-1), tags: ["t"]));

        var removed = await store.PruneAsync(Now.AddDays(-30));

        Assert.Equal(2, removed);
        Assert.Equal(["fresh"], (await QueryAsync(store, "t")).Select(i => i.Id));
    }

    [AzuriteFact]
    public async Task Invalidate_removes_every_registration_holding_the_handle()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("a", handle: "dead-token", tags: ["t"]));
        await store.UpsertAsync(Installation("b", PushPlatform.Android, handle: "dead-token", tags: ["t"]));
        await store.UpsertAsync(Installation("c", handle: "live-token", tags: ["t"]));

        var removed = await store.InvalidateAsync("dead-token");

        Assert.Equal(2, removed);
        Assert.Equal(["c"], (await QueryAsync(store, "t")).Select(i => i.Id));
    }
}
