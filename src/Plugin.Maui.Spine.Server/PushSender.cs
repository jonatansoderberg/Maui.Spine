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
    /// <remarks>
    /// Two roads on Apple, chosen per installation. One with a widget token — iOS 26 and the extension
    /// built with <c>SpineWidgetsPush</c> — gets the <c>widgets</c> push: WidgetKit reloads on its own and
    /// the app stays asleep, but every push-enabled widget reloads, whatever <paramref name="kind"/> says.
    /// One without gets the silent push as before, which names the kind but needs the app woken.
    /// </remarks>
    public async Task<PushResult> RefreshWidgetsAsync(PushTarget target, string? kind = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var byPlatform = await ResolveAsync(target, cancellationToken);
        if (byPlatform.Count == 0) return PushResult.Empty;

        var deliveries = new List<PushDelivery>();

        // A dead widget token says nothing about the device token, so those deliveries are reported
        // but never remove the installation; the app sends a fresh one at its next registration.
        var removable = new List<PushDelivery>();

        foreach (var (platform, installations) in byPlatform)
        {
            if (!_transports.TryGetValue(platform, out var transport)) continue;

            switch (platform)
            {
                case PushPlatform.Apple:
                    var pushed = installations
                        .Where(i => i.WidgetToken is { Length: > 0 })
                        .Select(i => i with { Handle = i.WidgetToken! })
                        .ToList();
                    if (pushed.Count > 0)
                        deliveries.AddRange(await transport.SendAsync(pushed, PushPayloads.ApnsWidgetPush(BundleId), cancellationToken));

                    var woken = installations.Where(i => i.WidgetToken is not { Length: > 0 }).ToList();
                    if (woken.Count > 0)
                    {
                        var sent = await transport.SendAsync(woken, PushPayloads.ApnsWidgetRefresh(kind, BundleId), cancellationToken);
                        deliveries.AddRange(sent);
                        removable.AddRange(sent);
                    }
                    break;

                case PushPlatform.Android:
                    var fcm = await transport.SendAsync(installations, PushPayloads.FcmWidgetRefresh(kind), cancellationToken);
                    deliveries.AddRange(fcm);
                    removable.AddRange(fcm);
                    break;
            }
        }

        await RemoveInvalidAsync(removable, cancellationToken);

        return new PushResult { Deliveries = deliveries };
    }

    /// <inheritdoc />
    public async Task<PushResult> BroadcastLiveActivityAsync(
        string channel, string kind, LiveActivityLayout layout, LiveActivityEvent @event = LiveActivityEvent.Update,
        LiveActivityOptions? options = null, ApnsEnvironment? environment = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        if (@event == LiveActivityEvent.Start)
            throw new ArgumentException(
                "A broadcast updates or ends the activities on a channel. Start one on it with StartLiveActivityAsync and LiveActivityOptions.Channel, or in the app.",
                nameof(@event));

        var now = _time.GetUtcNow();
        var deliveries = new List<PushDelivery>();

        foreach (var transport in _transports.Values.OfType<IPushBroadcastTransport>())
        {
            var envelope = transport.Platform switch
            {
                PushPlatform.Apple => PushPayloads.ApnsLiveActivity(kind, layout, @event, alert: null, options, BundleId, now) with { ApnsEnvironment = environment },
                PushPlatform.Android => PushPayloads.FcmLiveActivity(kind, layout, @event, (options ?? new LiveActivityOptions()) with { Channel = channel }),
                _ => null,
            };

            if (envelope is not null)
                deliveries.Add(await transport.BroadcastAsync(channel, envelope, cancellationToken));
        }

        return new PushResult { Deliveries = deliveries };
    }

    /// <summary>
    /// Said when the installation has no token for what is being sent. A Live Activity is addressed
    /// by its own token, which only exists while it runs — or by the push-to-start token, which the
    /// device only has on iOS 17.2 and later.
    /// </summary>
    private const string NoToken = "NoLiveActivityToken";

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
        }, cancellationToken, Address);

        // Apple's liveactivity topic accepts only the activity's own token: a device token there is
        // rejected with DeviceTokenNotForTopic. Android has no separate token — Spine renders the
        // activity as a notification, so the device token is the right one.
        string? Address(PushPlatform platform, PushInstallation installation) => platform switch
        {
            PushPlatform.Apple when @event == LiveActivityEvent.Start => installation.LiveActivities?.PushToStart,
            PushPlatform.Apple => installation.LiveActivities?.Activities.GetValueOrDefault(kind),
            _ => installation.Handle,
        };
    }

    /// <param name="address">
    /// Which token to send to, when it is not the device token. An installation it answers
    /// <see langword="null"/> for cannot be addressed at all and is reported rather than sent with
    /// the wrong one — the mistake that produces APNs' DeviceTokenNotForTopic.
    /// </param>
    private async Task<PushResult> DispatchAsync(
        PushTarget target,
        Func<PushPlatform, PushEnvelope?> build,
        CancellationToken cancellationToken,
        Func<PushPlatform, PushInstallation, string?>? address = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        var byPlatform = await ResolveAsync(target, cancellationToken);
        if (byPlatform.Count == 0) return PushResult.Empty;

        var deliveries = new List<PushDelivery>();

        foreach (var (platform, installations) in byPlatform)
        {
            if (!_transports.TryGetValue(platform, out var transport)) continue;
            if (build(platform) is not { } envelope) continue;

            var addressed = installations;

            if (address is not null)
            {
                addressed = [];
                foreach (var installation in installations)
                {
                    if (address(platform, installation) is { Length: > 0 } token)
                        addressed.Add(installation with { Handle = token });
                    else
                        deliveries.Add(new PushDelivery(installation.Id, platform, PushStatus.Failed, NoToken));
                }

                if (addressed.Count == 0) continue;
            }

            var sent = await transport.SendAsync(addressed, envelope, cancellationToken);
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
