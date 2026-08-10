using Microsoft.Extensions.Caching.Memory;

namespace Orientera.Backend.Caching;

/// <summary>
/// Time-to-live cache in front of the upstream calls, with single flight: a hundred phones
/// opening the same competition in the same second cause one Eventor request, not a hundred.
/// </summary>
/// <remarks>
/// What gets cached is the in-flight task, not its result — that is what makes the
/// deduplication work. A faulted task is evicted at once; a cached failure would turn one bad
/// minute upstream into a bad minute for every client.
/// </remarks>
public sealed class ResponseCache(IMemoryCache _cache)
{
    private readonly Lock _gate = new();

    public async Task<T> GetOrAddAsync<T>(
        string key,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> load,
        CancellationToken cancellationToken = default)
    {
        Task<T> shared;

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out Task<T>? existing) && existing is not null)
            {
                shared = existing;
            }
            else
            {
                // Detached from the caller's token: the shared load must not be cancelled by
                // whichever request happened to arrive first.
                shared = load(CancellationToken.None);
                _cache.Set(key, shared, lifetime);
            }
        }

        try
        {
            return await shared.WaitAsync(cancellationToken);
        }
        catch
        {
            if (shared.IsFaulted)
                _cache.Remove(key);

            throw;
        }
    }
}
