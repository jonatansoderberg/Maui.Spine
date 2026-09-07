using System.Text.Json;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// Reads back the JSON <see cref="FcmMessage.ToJson"/> writes. The payload layer produces JSON so
/// it can be inspected and tested on its own; the transport needs the values again to fill in the
/// SDK's own types.
/// </summary>
internal static class FcmMessageReader
{
    internal static FcmMessage Read(string json)
    {
        var message = new FcmMessage();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("data", out var data))
        {
            foreach (var field in data.EnumerateObject())
                message.Data[field.Name] = field.Value.GetString() ?? "";
        }

        if (!root.TryGetProperty("android", out var android)) return message;

        if (android.TryGetProperty("priority", out var priority))
            message.HighPriority = priority.GetString() == "high";

        if (android.TryGetProperty("collapse_key", out var collapse))
            message.CollapseKey = collapse.GetString();

        if (android.TryGetProperty("ttl", out var ttl) &&
            ttl.GetString() is { } text &&
            long.TryParse(text.TrimEnd('s'), out var seconds))
        {
            message.TimeToLive = TimeSpan.FromSeconds(seconds);
        }

        return message;
    }
}
