using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// What runs before every model call: who may call, how often, and whether the money is there. The money is
/// checked against the store's totals, so the call that crosses a limit still runs and the next one is refused.
/// </summary>
internal sealed class AIBudgetGate
{
    internal const string BudgetHeader = "Spine-AI-Budget";

    private readonly AIFeatureRegistry _registry;
    private readonly IAIUsageStore _store;
    private readonly SpineAIOptions _options;
    private readonly IServiceProvider _services;
    private readonly IHttpContextAccessor? _http;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<AIUsageScope, DateTimeOffset> _spentUntil = new();
    private readonly ConcurrentDictionary<string, Limiters> _limiters = new(StringComparer.OrdinalIgnoreCase);

    public AIBudgetGate(
        AIFeatureRegistry registry, IAIUsageStore store, SpineAIOptions options, IServiceProvider services,
        IHttpContextAccessor? http, TimeProvider time, ILogger logger)
    {
        _registry = registry;
        _store = store;
        _options = options;
        _services = services;
        _http = http;
        _time = time;
        _logger = logger;

        // New limits apply from the next call; the limiters built for the old ones are let go.
        registry.Rebuilt += () =>
        {
            foreach (var key in _limiters.Keys)
            {
                if (_limiters.TryRemove(key, out var old)) old.Dispose();
            }
        };
    }

    /// <summary>Whether a hard budget of the app or of <paramref name="feature"/> is known to be spent right now.</summary>
    public bool IsSpent(string feature)
    {
        var now = _time.GetUtcNow();
        return Spent(AIUsageScope.App, now) || Spent(AIUsageScope.ForFeature(feature), now);

        bool Spent(AIUsageScope scope, DateTimeOffset at) => _spentUntil.TryGetValue(scope, out var until) && at < until;
    }

    /// <summary>
    /// Takes a permit from the feature's rate and concurrency limits for <paramref name="caller"/>. The result is
    /// held until the call ends, a stream until its last update. A <paramref name="lane"/> has limiters of its own with the
    /// same limits, so a realtime session's function calls do not use up the caller's session starts.
    /// </summary>
    /// <exception cref="AIRateLimitedException">The caller is over a limit.</exception>
    public IDisposable Acquire(AIFeature feature, AICaller caller, string? lane = null)
    {
        if (caller.Kind == AICallerKind.System) return NoLease.Instance;

        var limiters = _limiters.GetOrAdd(lane is null ? feature.Name : $"{feature.Name}|{lane}", _ => new Limiters(feature.Options));

        if (limiters.Rate is { } rate)
        {
            using var permit = rate.AttemptAcquire(caller);
            if (!permit.IsAcquired)
                throw new AIRateLimitedException(feature.Name, permit.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1));
        }

        if (limiters.Concurrency is { } concurrency)
        {
            var running = concurrency.AttemptAcquire(caller);
            if (!running.IsAcquired)
            {
                running.Dispose();
                throw new AIRateLimitedException(feature.Name, null);
            }

            return running;
        }

        return NoLease.Instance;
    }

    /// <summary>Checks who may call and what is left to spend.</summary>
    /// <exception cref="AIFeatureOffException">The caller must sign in, or a hard budget is spent.</exception>
    public async Task CheckAsync(AIFeature feature, AICaller caller, CancellationToken cancellationToken)
    {
        if (feature.Options.EffectiveCallers == AICallers.SignedIn && caller.Kind is AICallerKind.Installation or AICallerKind.Anonymous)
            throw new AIFeatureOffException(feature.Name, AIOffReason.SignInRequired);

        var snapshot = _registry.Current;
        var now = _time.GetUtcNow();
        AIPeriod[] periods = [AIPeriod.Day(now, snapshot.Zone), AIPeriod.Month(now, snapshot.Zone)];

        await CheckScopeAsync(feature, AIUsageScope.App, snapshot.Configuration.Budget, periods, snapshot.Configuration.Currency, cancellationToken);
        await CheckScopeAsync(feature, AIUsageScope.ForFeature(feature.Name), feature.Options.Budget, periods, snapshot.Configuration.Currency, cancellationToken);

        if (feature.Options.Budget?.PerCaller?.For(caller.Kind) is { } mine)
            await CheckCallerAsync(feature, caller, mine, periods, cancellationToken);
    }

    private async Task CheckScopeAsync(
        AIFeature feature, AIUsageScope scope, AIBudgetLimits? limits, AIPeriod[] periods, string currency, CancellationToken cancellationToken)
    {
        if (limits is null) return;

        foreach (var period in periods)
        {
            var (soft, hard) = period.Kind == AIPeriodKind.Day ? (limits.DaySoft, limits.DayHard) : (limits.MonthSoft, limits.MonthHard);
            if (soft is null && hard is null) continue;

            var spent = (await _store.GetTotalsAsync(scope, period, cancellationToken)).Cost;

            if (hard is { } hardLimit && spent >= hardLimit)
            {
                _spentUntil.AddOrUpdate(scope, period.End, (_, until) => until > period.End ? until : period.End);
                await AlertAsync(new AIBudgetAlert(scope, period, AIBudgetLevel.Hard, spent, hardLimit, currency), cancellationToken);
                throw new AIFeatureOffException(feature.Name, AIOffReason.Budget);
            }

            if (soft is { } softLimit && spent >= softLimit)
            {
                if (_http?.HttpContext?.Response is { HasStarted: false } response)
                    response.Headers[BudgetHeader] = "soft";

                await AlertAsync(new AIBudgetAlert(scope, period, AIBudgetLevel.Soft, spent, softLimit, currency), cancellationToken);
            }
        }
    }

    private async Task CheckCallerAsync(AIFeature feature, AICaller caller, AICallerBudget limits, AIPeriod[] periods, CancellationToken cancellationToken)
    {
        var scope = AIUsageScope.ForCaller(feature.Name, caller);

        foreach (var period in periods)
        {
            var (hard, calls) = period.Kind == AIPeriodKind.Day ? (limits.DayHard, limits.DayCalls) : (limits.MonthHard, (long?)null);
            if (hard is null && calls is null) continue;

            var totals = await _store.GetTotalsAsync(scope, period, cancellationToken);
            if (totals.Cost >= hard || totals.Calls >= calls)
                throw new AIFeatureOffException(feature.Name, AIOffReason.Budget);
        }
    }

    private async Task AlertAsync(AIBudgetAlert alert, CancellationToken cancellationToken)
    {
        if (alert.Level == AIBudgetLevel.Hard)
            _logger.LogWarning("AI budget: {Alert}", alert);

        if (_options.OnBudgetThreshold is not { } onThreshold) return;

        try
        {
            if (!await _store.TryMarkAlertAsync(alert.Scope, alert.Period, alert.Level, cancellationToken)) return;
            await onThreshold(alert, _services, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogError(e, "AI budget: OnBudgetThreshold failed for '{Alert}'.", alert);
        }
    }

    private sealed class Limiters : IDisposable
    {
        public Limiters(AIFeatureOptions options)
        {
            if (options.RequestsPerMinute is { } rates)
            {
                Rate = PartitionedRateLimiter.Create<AICaller, string>(caller => rates.For(caller.Kind) is { } perMinute
                    ? RateLimitPartition.GetSlidingWindowLimiter(caller.Key, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = perMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter(caller.Key));
            }

            if (options.ConcurrentPerCaller is { } concurrent)
            {
                Concurrency = PartitionedRateLimiter.Create<AICaller, string>(caller =>
                    RateLimitPartition.GetConcurrencyLimiter(caller.Key, _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = concurrent,
                        QueueLimit = 0,
                    }));
            }
        }

        public PartitionedRateLimiter<AICaller>? Rate { get; }
        public PartitionedRateLimiter<AICaller>? Concurrency { get; }

        public void Dispose()
        {
            Rate?.Dispose();
            Concurrency?.Dispose();
        }
    }

    private sealed class NoLease : IDisposable
    {
        public static NoLease Instance { get; } = new();
        public void Dispose() { }
    }
}
