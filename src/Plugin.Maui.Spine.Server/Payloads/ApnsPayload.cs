using System.Text.Json;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// The body of an APNs request, as Spine builds it before handing it to the transport. Reachable
/// from <see cref="PushNotification.Apple"/> so a caller can add or override anything Spine does
/// not model.
/// </summary>
public sealed class ApnsPayload
{
    /// <summary>The alert's first line. Absent for a silent or Live Activity push.</summary>
    public string? Title { get; set; }

    /// <summary>The alert's body.</summary>
    public string? Body { get; set; }

    /// <summary>The badge to set on the app icon.</summary>
    public int? Badge { get; set; }

    /// <summary>The sound to play; <c>default</c> for the system sound.</summary>
    public string? Sound { get; set; }

    /// <summary>The id iOS groups notifications by, which Spine sets from the channel.</summary>
    public string? ThreadId { get; set; }

    /// <summary>Wakes the app in the background. Set for a silent push.</summary>
    public bool ContentAvailable { get; set; }

    /// <summary>
    /// Tells WidgetKit that the widgets' content changed, so it reloads them — iOS 26's <c>widgets</c>
    /// push. Written as <c>content-changed: true</c>.
    /// </summary>
    public bool ContentChanged { get; set; }

    /// <summary>The category whose buttons the notification shows.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Lets the app's Notification Service Extension change the notification before it is shown.
    /// Spine sets it when there is an image to fetch.
    /// </summary>
    public bool MutableContent { get; set; }

    /// <summary>One of <c>passive</c>, <c>active</c>, <c>time-sensitive</c>, <c>critical</c>.</summary>
    public string? InterruptionLevel { get; set; }

    /// <summary>Values delivered alongside <c>aps</c>, including the Spine keys.</summary>
    public IDictionary<string, string> Data { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The Live Activity half, when this is one; otherwise <see langword="null"/>.</summary>
    public ApnsLiveActivity? LiveActivity { get; set; }

    /// <summary>The payload as the JSON APNs expects.</summary>
    /// <returns>The serialized body.</returns>
    public string ToJson()
    {
        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartObject("aps");

            if (Title is not null || Body is not null)
            {
                w.WriteStartObject("alert");
                if (Title is not null) w.WriteString("title", Title);
                if (Body is not null) w.WriteString("body", Body);
                w.WriteEndObject();
            }

            if (Badge is { } badge) w.WriteNumber("badge", badge);
            if (Sound is not null) w.WriteString("sound", Sound);
            if (ThreadId is not null) w.WriteString("thread-id", ThreadId);
            if (ContentAvailable) w.WriteNumber("content-available", 1);
            if (Category is not null) w.WriteString("category", Category);
            if (ContentChanged) w.WriteBoolean("content-changed", true);
            if (MutableContent) w.WriteNumber("mutable-content", 1);
            if (InterruptionLevel is not null) w.WriteString("interruption-level", InterruptionLevel);

            if (LiveActivity is { } activity)
            {
                w.WriteNumber("timestamp", activity.Timestamp.ToUnixTimeSeconds());
                w.WriteString("event", activity.Event);
                if (activity.StaleAt is { } stale) w.WriteNumber("stale-date", stale.ToUnixTimeSeconds());
                if (activity.DismissAt is { } dismiss) w.WriteNumber("dismissal-date", dismiss.ToUnixTimeSeconds());

                // ActivityKit decodes content-state into the extension's ContentState, which holds the
                // layout as a single string — see SpineActivityAttributes.ContentState(json:). Writing
                // the layout object here instead produces JSON that cannot be decoded, and iOS answers
                // by rendering its placeholder: a frozen progress ring where the activity should be.
                w.WritePropertyName("content-state");
                w.WriteStartObject();
                w.WriteString("json", activity.ContentStateJson);
                w.WriteEndObject();
            }

            w.WriteEndObject();

            foreach (var (key, value) in Data) w.WriteString(key, value);

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>The Live Activity fields of an APNs payload.</summary>
/// <param name="Event">One of <c>start</c>, <c>update</c>, <c>end</c>.</param>
/// <param name="ContentStateJson">The serialized <see cref="Common.LiveActivityLayout"/>.</param>
/// <param name="Timestamp">When the content was produced; iOS drops updates that arrive out of order.</param>
/// <param name="StaleAt">When the content should be considered out of date.</param>
/// <param name="DismissAt">When an ended activity should disappear.</param>
public readonly record struct ApnsLiveActivity(
    string Event,
    string ContentStateJson,
    DateTimeOffset Timestamp,
    DateTimeOffset? StaleAt = null,
    DateTimeOffset? DismissAt = null);
