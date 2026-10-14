using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Plugin.Maui.Spine.Server.Messaging;

namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>Looks generated images up, and orders the missing ones.</summary>
/// <example>
/// <code>
/// // An app's own route, with its own key:
/// var asset = await assets.LookUpAsync("ArenaImage", new ArenaKey(eventId, season), await ai.IdentifyAsync(request), ct);
/// return asset.ToResult();
/// </code>
/// </example>
public interface IGeneratedAssets
{
    /// <summary>
    /// The image for <paramref name="key"/>: its URL when it is archived, otherwise an order and
    /// <see cref="GeneratedAssetStatus.Pending"/>. Never waits for a model.
    /// </summary>
    /// <param name="feature">The asset feature's name.</param>
    /// <param name="key">The key's text, as its <c>ToString()</c> writes it.</param>
    /// <param name="caller">Who asks; orders count against the caller's <c>OrdersPerMinute</c>. <see cref="AICaller.System"/> has no limit.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<GeneratedAsset> LookUpAsync(string feature, string key, AICaller caller, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="LookUpAsync(string, string, AICaller, CancellationToken)"/>
    Task<GeneratedAsset> LookUpAsync<TKey>(string feature, TKey key, AICaller caller, CancellationToken cancellationToken = default)
        where TKey : IParsable<TKey>;
}

/// <summary>The message that orders one image.</summary>
internal sealed record GenerateAsset(string Feature, int Version, string Key);

/// <summary>The subscriber that handles <see cref="GenerateAsset"/>, as declared on the messaging builder.</summary>
internal sealed record GeneratedAssetQueue(string Subscriber, int Attempts, TimeSpan FirstDelay, TimeSpan MaxDelay)
{
    /// <summary>A model call's allowance when judging whether an order outlasts its retries.</summary>
    public static readonly TimeSpan GenerationAllowance = TimeSpan.FromMinutes(3);

    /// <summary>How long an order can take at most: every attempt, and the longest waits between them with their jitter.</summary>
    public TimeSpan LongestRun =>
        Enumerable.Range(0, Attempts - 1).Aggregate(Attempts * GenerationAllowance, (sum, i) =>
            sum + TimeSpan.FromTicks((long)(Math.Min(FirstDelay.Ticks * Math.Pow(2, i), MaxDelay.Ticks) * 1.2)));
}

internal sealed class GeneratedAssets(
    SpineAIAssetsOptions options,
    AssetSettings settings,
    IGeneratedAssetStore store,
    IAIFeatures features,
    IMessagePublisher publisher,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<GeneratedAssets> logger) : IGeneratedAssets, IDisposable
{
    private readonly ExpiringCache<GeneratedAsset> _ready = new(time);
    private readonly ExpiringCache<bool> _ordered = new(time);
    private readonly Dictionary<string, (int? SignedIn, int? Installation, PartitionedRateLimiter<AICaller> Limiter)> _limiters = new(StringComparer.OrdinalIgnoreCase);
    // A limiter replaced after a reload may still be in use by a lookup, so it is disposed with the service, not at once.
    private readonly List<PartitionedRateLimiter<AICaller>> _replaced = [];
    private readonly ConcurrentDictionary<string, bool> _warnedKeys = new(StringComparer.OrdinalIgnoreCase);

    public TimeProvider Time => time;

    public bool IsExposed(string feature) =>
        options.Assets.TryGetValue(feature, out var asset) && settings.For(asset.Feature).Expose;

    public Task<GeneratedAsset> LookUpAsync<TKey>(string feature, TKey key, AICaller caller, CancellationToken cancellationToken = default)
        where TKey : IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(key);
        return LookUpAsync(feature, key.ToString() ?? "", caller, cancellationToken);
    }

    public async Task<GeneratedAsset> LookUpAsync(string feature, string key, AICaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(caller);

        if (!options.Assets.TryGetValue(feature, out var asset))
            return new GeneratedAsset(GeneratedAssetStatus.NotFound);

        if (asset.Canonical(key, out var parsed) is not { } canonical)
        {
            // A key type whose ToString() does not read back would make every lookup "not found" without a word.
            if (parsed && _warnedKeys.TryAdd(asset.Feature, true))
                logger.LogError("Generated assets: '{Key}' parses as a {KeyType}, but its ToString() does not parse back to it or is not a safe path. Override ToString() so that it writes the text Parse reads (letters, digits, '.', '_', '-', '/' between segments).", key, asset.KeyType.Name);
            return new GeneratedAsset(GeneratedAssetStatus.NotFound);
        }

        var config = settings.For(asset.Feature);
        var path = AssetPaths.Of(asset.Feature, config.Version, canonical);
        var imagePath = AssetPaths.Image(path, config);

        if (_ready.TryGet(imagePath, out var cached))
            return cached;

        if (await store.GetAsync(imagePath, cancellationToken) is { } stored)
        {
            var ready = new GeneratedAsset(GeneratedAssetStatus.Ready, AssetPaths.WithVersion(stored));
            _ready.Set(imagePath, ready, options.ReadyCacheTime);
            return ready;
        }

        var now = time.GetUtcNow();
        var order = $"{asset.Feature}|{config.Version}|{canonical}";

        if (await store.GetFailureAsync(path, cancellationToken) is { } failure)
        {
            if (now < failure.RetryAfter)
                return new GeneratedAsset(GeneratedAssetStatus.Failed, Failure: failure.Reason, RetryAfter: failure.RetryAfter);

            // The first order's id is still held for its window, so the order after a failure needs an id of its own.
            order += $"|{failure.FailedAt.UtcTicks}";
        }

        if (features.StateOf(asset.Feature) is var state and not AIFeatureState.On)
            return new GeneratedAsset(GeneratedAssetStatus.Off, Off: OffReasonOf(state));

        var pending = new GeneratedAsset(GeneratedAssetStatus.Pending, RetryAfter: now + options.PendingRetryAfter);
        if (_ordered.TryGet(order, out _))
            return pending;

        await using (var scope = scopes.CreateAsyncScope())
        {
            if (!await asset.AcceptsAsync(scope.ServiceProvider, canonical, cancellationToken))
                return new GeneratedAsset(GeneratedAssetStatus.NotFound);
        }

        if (caller.Kind != AICallerKind.System && LimiterOf(asset.Feature, config.OrdersPerMinute) is { } limiter)
        {
            using var lease = limiter.AttemptAcquire(caller);
            if (!lease.IsAcquired)
            {
                var wait = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromMinutes(1);
                return new GeneratedAsset(GeneratedAssetStatus.RateLimited, RetryAfter: now + wait);
            }
        }

        var published = await publisher.PublishAsync(
            new GenerateAsset(asset.Feature, config.Version, canonical),
            new PublishOptions { DeduplicationId = order, DeduplicationWindow = options.OrderWindow },
            cancellationToken);

        if (!published.IsDuplicate)
            logger.LogInformation("Generated assets: ordered {Path} for {Caller}.", imagePath, caller);

        _ordered.Set(order, true, options.OrderWindow);
        return pending;
    }

    internal static AIOffReason OffReasonOf(AIFeatureState state) => state switch
    {
        AIFeatureState.Disabled => AIOffReason.Disabled,
        AIFeatureState.BudgetSpent => AIOffReason.Budget,
        _ => AIOffReason.NotConfigured,
    };

    private PartitionedRateLimiter<AICaller>? LimiterOf(string feature, AICallerRates? rates)
    {
        if (rates is null || (rates.SignedIn is null && rates.Installation is null))
            return null;

        lock (_limiters)
        {
            var current = _limiters.GetValueOrDefault(feature);
            if (current.Limiter is not null && current.SignedIn == rates.SignedIn && current.Installation == rates.Installation)
                return current.Limiter;

            var limiter = PartitionedRateLimiter.Create<AICaller, string>(caller =>
                (caller.Kind == AICallerKind.SignedIn ? rates.SignedIn : rates.Installation ?? rates.SignedIn) is { } perMinute
                    ? RateLimitPartition.GetSlidingWindowLimiter(caller.Key, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = perMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter(caller.Key));

            _limiters[feature] = (rates.SignedIn, rates.Installation, limiter);
            if (current.Limiter is not null)
                _replaced.Add(current.Limiter);
            return limiter;
        }
    }

    public void Dispose()
    {
        lock (_limiters)
        {
            foreach (var limiter in _limiters.Values.Select(l => l.Limiter).Concat(_replaced))
                limiter.Dispose();
        }
    }
}

/// <summary>Where a feature's images live.</summary>
internal static class AssetPaths
{
    /// <summary><c>{feature}/v{version}/{key}</c>, without an extension; failures are noted under it.</summary>
    public static string Of(string feature, int version, string canonical) => $"{feature.ToLowerInvariant()}/v{version}/{canonical}";

    public static string Image(string path, GeneratedAssetOptions config) => $"{path}.{ExtensionOf(config.MediaType)}";

    public static string? TryExtensionOf(string? mediaType) => (mediaType ?? "image/png").ToLowerInvariant() switch
    {
        "image/png" => "png",
        "image/jpeg" or "image/jpg" => "jpg",
        "image/webp" => "webp",
        "image/gif" => "gif",
        _ => null,
    };

    public static string ExtensionOf(string? mediaType) =>
        TryExtensionOf(mediaType) ?? throw new InvalidOperationException($"Generated assets: '{mediaType}' is not an image type Spine knows. Use image/png, image/jpeg, image/webp or image/gif.");

    /// <summary>The URL with <c>?v=</c> from the image's last change, so a remade image under the same path gets a new URL.</summary>
    public static Uri WithVersion(StoredAsset stored)
    {
        // Not UriBuilder: its Uri keeps ":443" in OriginalString, which is what JSON writes.
        var url = stored.Url.AbsoluteUri;
        return new Uri($"{url}{(stored.Url.Query.Length > 0 ? '&' : '?')}v={stored.LastModified.ToUnixTimeSeconds()}");
    }
}

/// <summary>Each feature's <see cref="GeneratedAssetOptions"/>, bound once and again after a configuration reload.</summary>
internal sealed class AssetSettings
{
    private readonly IConfiguration _section;
    private ConcurrentDictionary<string, GeneratedAssetOptions> _bound = new(StringComparer.OrdinalIgnoreCase);

    public AssetSettings(IConfiguration section)
    {
        _section = section;
        ChangeToken.OnChange(section.GetReloadToken, () => _bound = new(StringComparer.OrdinalIgnoreCase));
    }

    public GeneratedAssetOptions For(string feature) =>
        _bound.GetOrAdd(feature, name => _section.GetSection($"Features:{name}:Asset").Get<GeneratedAssetOptions>() ?? new GeneratedAssetOptions());

    public string? KindOf(string feature) => _section[$"Features:{feature}:Kind"];
}

/// <summary>A small cache on the injected clock, so tests can move time.</summary>
internal sealed class ExpiringCache<T>(TimeProvider time)
{
    private const int MaxEntries = 10_000;
    private readonly ConcurrentDictionary<string, (T Value, DateTimeOffset Expires)> _entries = new(StringComparer.Ordinal);

    public bool TryGet(string key, out T value)
    {
        if (_entries.TryGetValue(key, out var entry) && time.GetUtcNow() < entry.Expires)
        {
            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(string key, T value, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
            return;

        var now = time.GetUtcNow();
        if (_entries.Count >= MaxEntries)
        {
            foreach (var (stale, _) in _entries.Where(e => e.Value.Expires <= now))
                _entries.TryRemove(stale, out _);
            if (_entries.Count >= MaxEntries)
                _entries.Clear();
        }

        _entries[key] = (value, now + lifetime);
    }
}
