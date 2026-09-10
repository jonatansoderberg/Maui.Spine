using System.Xml.Linq;
namespace Plugin.Maui.Spine.Server;

/// <summary>How hard the platform should try to deliver right away.</summary>
public enum PushPriority
{
    /// <summary>Delivered when the system finds it convenient. APNs 5, FCM normal.</summary>
    Normal,

    /// <summary>Delivered immediately, waking the device if need be. APNs 10, FCM high.</summary>
    High,
}

/// <summary>How much the notification is allowed to interrupt, on the platforms that ask.</summary>
public enum PushInterruption
{
    /// <summary>Added to the list without lighting the screen or making a sound.</summary>
    Passive,

    /// <summary>The default: lights the screen and may make a sound.</summary>
    Active,

    /// <summary>Breaks through Focus. Needs the time-sensitive entitlement on iOS.</summary>
    TimeSensitive,
}

/// <summary>The text shown when a Live Activity is started by push.</summary>
/// <param name="Title">The first line.</param>
/// <param name="Body">The second line.</param>
public readonly record struct PushAlert(string Title, string Body);

/// <summary>What a Live Activity push does to the activity.</summary>
public enum LiveActivityEvent
{
    /// <summary>Starts an activity that is not running (push-to-start, iOS 17.2+).</summary>
    Start,

    /// <summary>Replaces the content of a running activity.</summary>
    Update,

    /// <summary>Ends the activity.</summary>
    End,
}

/// <summary>The timing knobs of a Live Activity push.</summary>
public sealed record LiveActivityOptions
{
    /// <summary>
    /// When the shown content should be considered out of date. The system dims it after this,
    /// rather than showing a stale time as if it were current.
    /// </summary>
    public DateTimeOffset? StaleAt { get; init; }

    /// <summary>When the system should remove an ended activity from the Lock Screen.</summary>
    public DateTimeOffset? DismissAt { get; init; }

    /// <summary>
    /// Delivery priority. The default is <see cref="PushPriority.Normal"/>, which is APNs 5:
    /// priority 10 counts against the hourly budget, so ask for it only when the update cannot wait.
    /// </summary>
    public PushPriority Priority { get; init; } = PushPriority.Normal;

    /// <summary>
    /// The channel an activity started by push follows, so that later broadcasts to it reach this
    /// activity too. iOS 18 reads it as <c>input-push-channel</c>; Android subscribes to the channel's
    /// FCM topic. Only meaningful with <see cref="LiveActivityEvent.Start"/>.
    /// </summary>
    public string? Channel { get; init; }
}

/// <summary>A user-visible notification, in the platform-neutral form Spine sends.</summary>
/// <remarks>
/// One of these becomes an <c>alert</c> on APNs and a high-priority data message on FCM, so the
/// app's handler sees the same thing on both. The platform hooks are there for the last ten percent.
/// </remarks>
public sealed record PushNotification
{
    /// <summary>The first line.</summary>
    public required string Title { get; init; }

    /// <summary>The body text.</summary>
    public required string Body { get; init; }

    /// <summary>The page to open when the notification is tapped. Sent as <see cref="Common.PushKeys.Route"/>.</summary>
    public string? Route { get; init; }

    /// <summary>The Android notification channel, and the thread id iOS groups by.</summary>
    public string? Channel { get; init; }

    /// <summary>Replaces any undelivered notification with the same id. <c>apns-collapse-id</c> and <c>collapse_key</c>.</summary>
    public string? CollapseId { get; init; }

    /// <summary>How long the platform may keep trying. Unset means the platform's default.</summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>Delivery priority; <see cref="PushPriority.High"/> for something the user is waiting for.</summary>
    public PushPriority Priority { get; init; } = PushPriority.High;

    /// <summary>How much it may interrupt.</summary>
    public PushInterruption Interruption { get; init; } = PushInterruption.Active;

    /// <summary>The badge number to set, or <see langword="null"/> to leave it alone.</summary>
    public int? Badge { get; init; }

    /// <summary>
    /// The sound to play; <c>default</c> for the system sound. Apple only: on Android the channel
    /// decides the sound, once, when the app creates it — see <c>AddChannel</c> in the app package.
    /// </summary>
    public string? Sound { get; init; }

    /// <summary>
    /// The buttons to show, by the id the app declared with <c>AddCategory</c>. Sent as
    /// <c>aps.category</c> and as <see cref="Common.PushKeys.Category"/>. An id the app never declared
    /// arrives without buttons rather than failing.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// A picture to show with the notification. Must be <c>https</c>. Android always shows it; iOS
    /// shows it only when the app is built with <c>SpinePushImages=true</c>, which adds the
    /// Notification Service Extension that fetches it — without that the notification arrives as text.
    /// </summary>
    public Uri? Image { get; init; }

    /// <summary>Extra values handed to the app's handler alongside the Spine keys.</summary>
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();

    /// <summary>Adjusts the APNs payload after Spine has built it.</summary>
    public Action<ApnsPayload>? Apple { get; init; }

    /// <summary>Adjusts the FCM message after Spine has built it.</summary>
    public Action<FcmMessage>? Android { get; init; }

    /// <summary>
    /// Adjusts the WNS toast after Spine built it — buttons, an attribution line, a scenario — for what
    /// the other properties do not cover. The element is the <c>toast</c> root.
    /// </summary>
    public Action<XElement>? Windows { get; init; }
}
