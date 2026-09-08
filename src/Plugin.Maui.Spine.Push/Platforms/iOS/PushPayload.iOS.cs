using Foundation;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Push;

/// <summary>Reads an APNs payload into the platform-neutral <see cref="PushMessage"/>.</summary>
internal static class PushPayload
{
    /// <summary>Flattens <paramref name="userInfo"/> and pulls the title and body out of <c>aps</c>.</summary>
    /// <param name="userInfo">The payload as APNs delivered it.</param>
    /// <returns>The message the app's handler sees.</returns>
    internal static PushMessage Read(NSDictionary userInfo)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in userInfo)
        {
            if (key.ToString() is not { } name || name == "aps") continue;
            data[name] = value?.ToString() ?? "";
        }

        // The alert text lives under aps, not beside it, so the Spine keys are only there when the
        // sender is Spine.Push. Fall back to what APNs itself carries, so a hand-written payload works.
        if (userInfo["aps"] is NSDictionary aps && aps["alert"] is NSDictionary alert)
        {
            if (!data.ContainsKey(PushKeys.Title) && alert["title"]?.ToString() is { } title)
                data[PushKeys.Title] = title;

            if (!data.ContainsKey(PushKeys.Body) && alert["body"]?.ToString() is { } body)
                data[PushKeys.Body] = body;
        }

        return PushMessage.From(data);
    }
}
