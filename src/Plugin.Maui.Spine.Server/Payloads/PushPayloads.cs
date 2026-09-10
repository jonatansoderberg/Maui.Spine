using System.Text;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// Turns one platform-neutral message into the shape each service expects. One
/// <see cref="PushNotification"/> becomes an <c>alert</c> on APNs and a data-only message on FCM,
/// so the app's handler sees the same thing on both.
/// </summary>
public static class PushPayloads
{
    /// <summary>The largest payload APNs accepts, for both notifications and Live Activities.</summary>
    public const int ApnsPayloadLimit = 4096;

    /// <summary>Builds the APNs request for a user-visible notification.</summary>
    /// <param name="notification">The message to send.</param>
    /// <param name="bundleId">The app's bundle id, used as the topic.</param>
    /// <param name="now">The current time, for the expiration header.</param>
    /// <returns>The envelope the APNs transport sends.</returns>
    public static PushEnvelope Apns(PushNotification notification, string bundleId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var payload = new ApnsPayload
        {
            Title = notification.Title,
            Body = notification.Body,
            Badge = notification.Badge,
            Sound = notification.Sound,
            ThreadId = notification.Channel,
            InterruptionLevel = Interruption(notification.Interruption),
            Category = notification.Category,
            MutableContent = notification.Image is not null,
        };

        Fill(payload.Data, notification, PushKeys.Kinds.Alert);
        notification.Apple?.Invoke(payload);

        return new PushEnvelope
        {
            Json = Guard(payload.ToJson()),
            ApnsPushType = "alert",
            ApnsTopic = bundleId,
            Priority = ApnsPriority(notification.Priority),
            CollapseId = notification.CollapseId,
            Expiration = notification.TimeToLive is { } ttl ? now + ttl : null,
        };
    }

    /// <summary>Builds the FCM message for a user-visible notification.</summary>
    /// <param name="notification">The message to send.</param>
    /// <returns>The envelope the FCM transport sends.</returns>
    public static PushEnvelope Fcm(PushNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var message = new FcmMessage
        {
            HighPriority = notification.Priority == PushPriority.High,
            TimeToLive = notification.TimeToLive,
            CollapseKey = notification.CollapseId,
        };

        Fill(message.Data, notification, PushKeys.Kinds.Alert);
        notification.Android?.Invoke(message);

        return new PushEnvelope
        {
            Json = message.ToJson(),
            Priority = message.HighPriority ? 10 : 5,
            CollapseId = message.CollapseKey,
            Expiration = null,
        };
    }

    /// <summary>Builds the APNs request for a silent push, which wakes the app without showing anything.</summary>
    /// <param name="data">What the handler should receive.</param>
    /// <param name="bundleId">The app's bundle id, used as the topic.</param>
    /// <returns>The envelope the APNs transport sends.</returns>
    /// <remarks>Apple requires priority 5 for background pushes, so that is what this sets.</remarks>
    public static PushEnvelope ApnsSilent(IReadOnlyDictionary<string, string> data, string bundleId)
    {
        ArgumentNullException.ThrowIfNull(data);

        var payload = new ApnsPayload { ContentAvailable = true };
        payload.Data[PushKeys.Kind] = PushKeys.Kinds.Silent;
        foreach (var (key, value) in data) payload.Data[key] = value;

        return new PushEnvelope
        {
            Json = Guard(payload.ToJson()),
            ApnsPushType = "background",
            ApnsTopic = bundleId,
            Priority = 5,
        };
    }

    /// <summary>Builds the FCM message for a silent push.</summary>
    /// <param name="data">What the handler should receive.</param>
    /// <returns>The envelope the FCM transport sends.</returns>
    /// <remarks>
    /// Sent at normal priority. A silent message is not worth waking the device for, and FCM's own
    /// guidance is to reserve high priority for something the user is waiting on.
    /// </remarks>
    public static PushEnvelope FcmSilent(IReadOnlyDictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var message = new FcmMessage { HighPriority = false };
        message.Data[PushKeys.Kind] = PushKeys.Kinds.Silent;
        foreach (var (key, value) in data) message.Data[key] = value;

        return new PushEnvelope { Json = message.ToJson(), Priority = 5 };
    }

    /// <summary>Builds the APNs request that starts, updates, or ends a Live Activity.</summary>
    /// <param name="kind">The kind the activity was started with.</param>
    /// <param name="layout">The trees to render.</param>
    /// <param name="event">What this push does to the activity.</param>
    /// <param name="alert">The text shown when starting by push; ignored otherwise.</param>
    /// <param name="options">Timing and priority; defaults when omitted.</param>
    /// <param name="bundleId">The app's bundle id; the topic gets the Live Activity suffix.</param>
    /// <param name="now">The current time, written as the payload's timestamp.</param>
    /// <returns>The envelope the APNs transport sends.</returns>
    /// <exception cref="InvalidOperationException">The layout does not fit in <see cref="ApnsPayloadLimit"/>.</exception>
    public static PushEnvelope ApnsLiveActivity(
        string kind,
        LiveActivityLayout layout,
        LiveActivityEvent @event,
        PushAlert? alert,
        LiveActivityOptions? options,
        string bundleId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(layout);

        options ??= new LiveActivityOptions();

        var payload = new ApnsPayload
        {
            LiveActivity = new ApnsLiveActivity(
                Event: @event switch
                {
                    LiveActivityEvent.Start => "start",
                    LiveActivityEvent.End => "end",
                    _ => "update",
                },
                ContentStateJson: layout.ToJson(),
                Timestamp: now,
                StaleAt: options.StaleAt,
                DismissAt: options.DismissAt),
        };

        if (@event == LiveActivityEvent.Start && alert is { } text)
        {
            payload.Title = text.Title;
            payload.Body = text.Body;
        }

        payload.Data[PushKeys.Kind] = PushKeys.Kinds.LiveActivity;
        payload.Data[PushKeys.Activity] = kind;

        return new PushEnvelope
        {
            Json = Guard(payload.ToJson()),
            ApnsPushType = "liveactivity",
            ApnsTopic = $"{bundleId}.push-type.liveactivity",
            Priority = ApnsPriority(options.Priority),
        };
    }

    /// <summary>Builds the FCM message that drives an Android Live Update.</summary>
    /// <param name="kind">The kind the activity was started with.</param>
    /// <param name="layout">The trees to render.</param>
    /// <param name="event">What this push does to the activity.</param>
    /// <returns>The envelope the FCM transport sends.</returns>
    /// <remarks>
    /// High priority: the message drives something the user is looking at, and the app's service has
    /// to run even when nothing is in the foreground.
    /// </remarks>
    /// <param name="options">Timing; only <see cref="LiveActivityOptions.StaleAt"/> means anything here.</param>
    public static PushEnvelope FcmLiveActivity(
        string kind, LiveActivityLayout layout, LiveActivityEvent @event, LiveActivityOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(layout);

        var message = new FcmMessage { HighPriority = true };
        message.Data[PushKeys.Kind] = PushKeys.Kinds.LiveActivity;
        message.Data[PushKeys.Activity] = kind;
        message.Data[PushKeys.Layout] = layout.ToJson();
        message.Data["spine.event"] = @event switch
        {
            LiveActivityEvent.Start => "start",
            LiveActivityEvent.End => "end",
            _ => "update",
        };

        if (options?.StaleAt is { } stale)
            message.Data["spine.stale"] = stale.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new PushEnvelope { Json = message.ToJson(), Priority = 10 };
    }

    /// <summary>Builds the APNs request that asks the app to rebuild its widgets.</summary>
    /// <param name="kind">The widget kind, or <see langword="null"/> for all of them.</param>
    /// <param name="bundleId">The app's bundle id, used as the topic.</param>
    /// <returns>The envelope the APNs transport sends.</returns>
    /// <remarks>
    /// A silent push that wakes the app, whose handler rebuilds the widgets — best effort by design:
    /// silent pushes are throttled, not delivered after a force-quit, and never in the simulator. The
    /// fallback for an installation without a widget token; see <see cref="ApnsWidgetPush"/>.
    /// </remarks>
    public static PushEnvelope ApnsWidgetRefresh(string? kind, string bundleId)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PushKeys.Kind] = PushKeys.Kinds.Widget,
        };
        if (kind is not null) data[PushKeys.Widget] = kind;

        var payload = new ApnsPayload { ContentAvailable = true };
        foreach (var (key, value) in data) payload.Data[key] = value;

        return new PushEnvelope
        {
            Json = Guard(payload.ToJson()),
            ApnsPushType = "background",
            ApnsTopic = bundleId,
            Priority = 5,
        };
    }

    /// <summary>
    /// Builds iOS 26's widget push: WidgetKit reloads the app's widgets itself, without waking the app.
    /// Sent to the installation's widget token, not its device token.
    /// </summary>
    /// <param name="bundleId">The app's bundle id; the topic is <c>&lt;bundle id&gt;.push-type.widgets</c>.</param>
    /// <returns>The envelope the APNs transport sends.</returns>
    /// <remarks>
    /// It cannot name a kind. The token covers every widget that registered the push handler, and a
    /// push reloads all of them — each reload counted against the widget's budget.
    /// </remarks>
    public static PushEnvelope ApnsWidgetPush(string bundleId) => new()
    {
        Json = Guard(new ApnsPayload { ContentChanged = true }.ToJson()),
        ApnsPushType = "widgets",
        ApnsTopic = $"{bundleId}.push-type.widgets",
        Priority = 10,
    };

    /// <summary>Builds the FCM message that asks the app to rebuild its widgets.</summary>
    /// <param name="kind">The widget kind, or <see langword="null"/> for all of them.</param>
    /// <returns>The envelope the FCM transport sends.</returns>
    public static PushEnvelope FcmWidgetRefresh(string? kind)
    {
        var message = new FcmMessage { HighPriority = true };
        message.Data[PushKeys.Kind] = PushKeys.Kinds.Widget;
        if (kind is not null) message.Data[PushKeys.Widget] = kind;

        return new PushEnvelope { Json = message.ToJson(), Priority = 10 };
    }

    private static void Fill(IDictionary<string, string> data, PushNotification notification, string kind)
    {
        data[PushKeys.Kind] = kind;
        data[PushKeys.Title] = notification.Title;
        data[PushKeys.Body] = notification.Body;
        if (notification.Route is { } route) data[PushKeys.Route] = route;
        if (notification.Channel is { } channel) data[PushKeys.Channel] = channel;
        if (notification.CollapseId is { } collapse) data[PushKeys.Collapse] = collapse;
        if (notification.Category is { } category) data[PushKeys.Category] = category;

        if (notification.Image is { } image)
        {
            // Refused here rather than dropped on the device: App Transport Security blocks http in the
            // extension, and a picture that silently never shows is the harder bug to find.
            if (image.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException($"PushNotification.Image must be https; got '{image}'.");

            data[PushKeys.Image] = image.ToString();
        }

        foreach (var (key, value) in notification.Data) data[key] = value;
    }

    private static int ApnsPriority(PushPriority priority) => priority == PushPriority.High ? 10 : 5;

    private static string? Interruption(PushInterruption interruption) => interruption switch
    {
        PushInterruption.Passive => "passive",
        PushInterruption.TimeSensitive => "time-sensitive",
        _ => "active",
    };

    private static string Guard(string json)
    {
        var size = Encoding.UTF8.GetByteCount(json);
        return size <= ApnsPayloadLimit
            ? json
            : throw new InvalidOperationException(
                $"The APNs payload is {size} bytes, over the {ApnsPayloadLimit}-byte limit. " +
                "Shorten the text, or send fewer nodes in the Live Activity layout.");
    }
}
