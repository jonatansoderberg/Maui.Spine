using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class InMemoryPushInstallationStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

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

    private static (InMemoryPushInstallationStore Store, FakeTimeProvider Time) NewStore()
    {
        var time = new FakeTimeProvider(Now);
        return (new InMemoryPushInstallationStore(time), time);
    }

    private static async Task<List<PushInstallation>> QueryAsync(
        IPushInstallationStore store, string expression, IReadOnlyCollection<PushPlatform>? platforms = null)
    {
        var found = new List<PushInstallation>();
        await foreach (var i in store.QueryAsync(PushTagExpression.Parse(expression), platforms))
            found.Add(i);
        return found;
    }

    [Fact]
    public async Task Upsert_replaces_the_registration_with_the_same_id()
    {
        var (store, _) = NewStore();

        await store.UpsertAsync(Installation("a", handle: "first"));
        await store.UpsertAsync(Installation("a", handle: "second"));

        Assert.Equal(1, store.Count);
        Assert.Equal("second", (await store.GetAsync("a"))!.Handle);
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_id_and_delete_is_forgiving()
    {
        var (store, _) = NewStore();

        Assert.Null(await store.GetAsync("nope"));
        await store.DeleteAsync("nope");
    }

    [Fact]
    public async Task Query_returns_the_matching_registrations()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("a", tags: ["kind:pm", "competition:1"]));
        await store.UpsertAsync(Installation("b", tags: ["kind:pm", "competition:2"]));
        await store.UpsertAsync(Installation("c", tags: ["kind:results", "competition:1"]));

        var found = await QueryAsync(store, "kind:pm && competition:1");

        Assert.Equal(["a"], found.Select(i => i.Id));
    }

    [Fact]
    public async Task Query_can_be_narrowed_to_platforms()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("apple", PushPlatform.Apple, tags: ["t"]));
        await store.UpsertAsync(Installation("android", PushPlatform.Android, tags: ["t"]));

        var found = await QueryAsync(store, "t", [PushPlatform.Android]);

        Assert.Equal(["android"], found.Select(i => i.Id));
    }

    [Fact]
    public async Task Query_skips_expired_registrations()
    {
        var (store, time) = NewStore();
        await store.UpsertAsync(Installation("live", tags: ["t"]));
        await store.UpsertAsync(Installation("dead", expiresAt: Now.AddHours(1), tags: ["t"]));

        Assert.Equal(2, (await QueryAsync(store, "t")).Count);

        time.SetUtcNow(Now.AddHours(2));

        Assert.Equal(["live"], (await QueryAsync(store, "t")).Select(i => i.Id));
    }

    [Fact]
    public async Task Match_all_reaches_every_live_registration()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("a"));
        await store.UpsertAsync(Installation("b", tags: ["t"]));

        var found = new List<PushInstallation>();
        await foreach (var i in store.QueryAsync(PushTagExpression.MatchAll)) found.Add(i);

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task Prune_removes_the_stale_and_the_expired()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("fresh", updatedAt: Now));
        await store.UpsertAsync(Installation("stale", updatedAt: Now.AddDays(-90)));
        await store.UpsertAsync(Installation("expired", updatedAt: Now, expiresAt: Now.AddMinutes(-1)));

        var removed = await store.PruneAsync(Now.AddDays(-30));

        Assert.Equal(2, removed);
        Assert.Equal(1, store.Count);
        Assert.NotNull(await store.GetAsync("fresh"));
    }

    [Fact]
    public async Task Invalidate_removes_every_registration_holding_the_handle()
    {
        var (store, _) = NewStore();
        await store.UpsertAsync(Installation("a", handle: "dead-token"));
        await store.UpsertAsync(Installation("b", handle: "dead-token"));
        await store.UpsertAsync(Installation("c", handle: "live-token"));

        var removed = await store.InvalidateAsync("dead-token");

        Assert.Equal(2, removed);
        Assert.Equal(1, store.Count);
        Assert.NotNull(await store.GetAsync("c"));
    }
}
