using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// Resolves a target against the register, builds one payload per platform, and hands each batch to
/// its transport. Installations a transport reports dead are removed on the way out.
/// </summary>
/// <param name="store">The register to resolve targets against.</param>
/// <param name="transports">The configured transports, one per platform at most.</param>
/// <param name="options">The server's settings; the Apple bundle id comes from here.</param>
/// <param name="timeProvider">The clock used for timestamps and expirations.</param>
public sealed class PushSender(
    IPushInstallationStore store,
    IEnumerable<IPushTransport> transports,
    SpinePushOptions options,
    TimeProvider? timeProvider = null) : IPushSender
{
    private readonly Dictionary<PushPlatform, IPushTransport> _transports =
        transports.ToDictionary(t => t.Platform);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<PushResult> SendAsync(PushTarget target, PushNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var now = _time.GetUtcNow();

        return DispatchAsync(target, platform => platform switch
        {
            PushPlatform.Apple => PushPayloads.Apns(notification, BundleId, now),
            PushPlatform.Android => PushPayloads.Fcm(notification),
            _ => null,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PushResult> SendSilentAsync(PushTarget target, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        return DispatchAsync(target, platform => platform switch
        {
            PushPlatform.Apple => PushPayloads.ApnsSilent(data, BundleId),
            PushPlatform.Android => PushPayloads.FcmSilent(data),
            _ => null,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PushResult> StartLiveActivityAsync(PushTarget target, string kind, LiveActivityLayout layout, PushAlert alert, LiveActivityOptions? options = null, CancellationToken cancellationToken = default) =>
        LiveActivityAsync(target, kind, layout, LiveActivityEvent.Start, alert, options, cancellationToken);

    /// <inheritdoc />
    public Task<PushResult> UpdateLiveActivityAsync(PushTarget target, string kind, LiveActivityLayout layout, LiveActivityEvent @event = LiveActivityEvent.Update, LiveActivityOptions? options = null, CancellationToken cancellationToken = default) =>
        LiveActivityAsync(target, kind, layout, @event, alert: null, options, cancellationToken);

    /// <inheritdoc />
    public Task<PushResult> RefreshWidgetsAsync(PushTarget target, string? kind = null, CancellationToken cancellationToken = default) =>
        DispatchAsync(target, platform => platform switch
        {
            PushPlatform.Apple => PushPayloads.ApnsWidgetRefresh(kind, BundleId),
            PushPlatform.Android => PushPayloads.FcmWidgetRefresh(kind),
            _ => null,
        }, cancellationToken);

    private string BundleId =>
        options.AppleOptions?.BundleId
        ?? throw new InvalidOperationException("Apple(...) is not configured, so no bundle id is available.");

    private Task<PushResult> LiveActivityAsync(
        PushTarget target, string kind, LiveActivityLayout layout, LiveActivityEvent @event,
        PushAlert? alert, LiveActivityOptions? activityOptions, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();

        return DispatchAsync(target, platform => platform switch
        {
            PushPlatform.Apple => PushPayloads.ApnsLiveActivity(kind, layout, @event, alert, activityOptions, BundleId, now),
            PushPlatform.Android => PushPayloads.FcmLiveActivity(kind, layout, @event, activityOptions),
            _ => null,
        }, cancellationToken);
    }

    private async Task<PushResult> DispatchAsync(
        PushTarget target, Func<PushPlatform, PushEnvelope?> build, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var byPlatform = await ResolveAsync(target, cancellationToken);
        if (byPlatform.Count == 0) return PushResult.Empty;

        var deliveries = new List<PushDelivery>();

        foreach (var (platform, installations) in byPlatform)
        {
            if (!_transports.TryGetValue(platform, out var transport)) continue;
            if (build(platform) is not { } envelope) continue;

            var sent = await transport.SendAsync(installations, envelope, cancellationToken);
            deliveries.AddRange(sent);
        }

        await RemoveInvalidAsync(deliveries, cancellationToken);

        return new PushResult { Deliveries = deliveries };
    }

    private async Task<Dictionary<PushPlatform, List<PushInstallation>>> ResolveAsync(
        PushTarget target, CancellationToken cancellationToken)
    {
        var byPlatform = new Dictionary<PushPlatform, List<PushInstallation>>();

        if (target.InstallationId is { } id)
        {
            var one = await store.GetAsync(id, cancellationToken);
            if (one is not null && !one.IsExpired(_time.GetUtcNow())) Add(one);
            return byPlatform;
        }

        var platforms = _transports.Keys.ToArray();
        await foreach (var installation in store.QueryAsync(target.Expression!, platforms, cancellationToken))
            Add(installation);

        return byPlatform;

        void Add(PushInstallation installation)
        {
            if (!byPlatform.TryGetValue(installation.Platform, out var list))
                byPlatform[installation.Platform] = list = [];
            list.Add(installation);
        }
    }

    private async Task RemoveInvalidAsync(List<PushDelivery> deliveries, CancellationToken cancellationToken)
    {
        foreach (var delivery in deliveries)
        {
            if (delivery.Status != PushStatus.Invalid) continue;
            await store.DeleteAsync(delivery.InstallationId, cancellationToken);
        }
    }
}
