using System.Text.Json;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// An FCM message as Spine builds it, before the transport hands it to the Firebase SDK. Reachable
/// from <see cref="PushNotification.Android"/>.
/// </summary>
/// <remarks>
/// Spine always sends data-only, never FCM's <c>notification</c> block: the package draws the
/// notification itself, so foreground and background behave alike and Spine picks the channel
/// (§5.2). Title and body therefore travel in <see cref="Data"/> under the Spine keys.
/// </remarks>
public sealed class FcmMessage
{
    /// <summary>Everything the app's handler receives, including the Spine keys.</summary>
    public IDictionary<string, string> Data { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Whether to send at high priority, which wakes the device out of Doze. Alerts do; silent
    /// messages do not, since a normal-priority data message may be delayed by battery optimization.
    /// </summary>
    public bool HighPriority { get; set; } = true;

    /// <summary>How long FCM may hold the message. Unset means FCM's default of four weeks.</summary>
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>Replaces any undelivered message with the same key.</summary>
    public string? CollapseKey { get; set; }

    /// <summary>The message as JSON, for logging and tests. The transport sends it through the SDK.</summary>
    /// <returns>The serialized message.</returns>
    public string ToJson()
    {
        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();

            w.WriteStartObject("data");
            foreach (var (key, value) in Data) w.WriteString(key, value);
            w.WriteEndObject();

            w.WriteStartObject("android");
            w.WriteString("priority", HighPriority ? "high" : "normal");
            if (TimeToLive is { } ttl) w.WriteString("ttl", $"{(long)ttl.TotalSeconds}s");
            if (CollapseKey is not null) w.WriteString("collapse_key", CollapseKey);
            w.WriteEndObject();

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
