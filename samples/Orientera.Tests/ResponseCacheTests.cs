using Microsoft.Extensions.Caching.Memory;
using Orientera.Backend.Caching;

namespace Orientera.Tests;

/// <summary>
/// The cache is what keeps Orientera from being a load problem for a federation's shared
/// service — and what keeps the app fast. Both properties are behaviour, so both are tested.
/// </summary>
public class ResponseCacheTests
{
    private readonly ResponseCache _cache = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task A_cached_answer_is_not_fetched_again()
    {
        int calls = 0;

        for (int i = 0; i < 3; i++)
            await _cache.GetOrAddAsync("events", TimeSpan.FromMinutes(5), _ => Load(ref calls));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Different_keys_are_different_answers()
    {
        int calls = 0;

        await _cache.GetOrAddAsync("events:aug", TimeSpan.FromMinutes(5), _ => Load(ref calls));
        await _cache.GetOrAddAsync("events:sep", TimeSpan.FromMinutes(5), _ => Load(ref calls));

        Assert.Equal(2, calls);
    }

    /// <summary>A hundred phones opening the same competition must not become a hundred requests.</summary>
    [Fact]
    public async Task Concurrent_callers_share_one_upstream_call()
    {
        int calls = 0;
        var gate = new TaskCompletionSource();

        var callers = Enumerable.Range(0, 50).Select(_ => _cache.GetOrAddAsync(
            "results",
            TimeSpan.FromMinutes(5),
            async _ =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return "list";
            }));

        var all = Task.WhenAll(callers);
        gate.SetResult();

        Assert.All(await all, value => Assert.Equal("list", value));
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// A cached failure would turn one bad minute upstream into a bad minute for everyone.
    /// </summary>
    [Fact]
    public async Task A_failure_is_never_the_cached_answer()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _cache.GetOrAddAsync<string>(
            "events",
            TimeSpan.FromMinutes(5),
            _ => throw new InvalidOperationException("Eventor svarade 503.")));

        var second = await _cache.GetOrAddAsync("events", TimeSpan.FromMinutes(5), _ => Task.FromResult("list"));

        Assert.Equal("list", second);
    }

    [Fact]
    public async Task An_expired_answer_is_fetched_again()
    {
        int calls = 0;

        await _cache.GetOrAddAsync("events", TimeSpan.FromMilliseconds(30), _ => Load(ref calls));
        await Task.Delay(80);
        await _cache.GetOrAddAsync("events", TimeSpan.FromMilliseconds(30), _ => Load(ref calls));

        Assert.Equal(2, calls);
    }

    private static Task<string> Load(ref int calls)
    {
        Interlocked.Increment(ref calls);
        return Task.FromResult("list");
    }
}
