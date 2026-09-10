using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>
/// The data bag a scheduled notification carries. It is the same shape a push arrives in, which is
/// what lets an opened local notification take the existing path to <see cref="IPushHandler"/>: the
/// platform hands the bag back — iOS through the notification's user info, Android through the
/// launch intent's extras — and <see cref="PushMessage.From"/> reads it without knowing the
/// difference.
/// </summary>
internal static class LocalNotificationPayload
{
    /// <summary>
    /// When the notification is meant to arrive, as unix seconds. Not part of <see cref="PushKeys"/>:
    /// it is how Spine reads a plan back, not something a handler needs.
    /// </summary>
    /// <remarks>
    /// Apple cannot be asked. <c>UNTimeIntervalNotificationTrigger.NextTriggerDate</c> answers
    /// <em>now plus the interval</em> every time it is read, so a pending notification appears to move
    /// further away the more often you look at it. The instant has to travel with the notification.
    /// </remarks>
    internal const string At = "spine.at";

    /// <summary>The sound, so a plan read back from Apple says what it was scheduled with.</summary>
    internal const string Sound = "spine.sound";

    /// <summary>Writes <paramref name="notification"/> as the keys the handler reads.</summary>
    internal static Dictionary<string, string> Write(LocalNotification notification)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);

        // The app's own keys first, so Spine's cannot be overwritten by one that happens to collide.
        foreach (var (key, value) in notification.Data ?? new Dictionary<string, string>())
            data[key] = value;

        data[PushKeys.Kind] = PushKeys.Kinds.Alert;
        data[PushKeys.Source] = PushKeys.Sources.Local;
        data[PushKeys.Title] = notification.Title;

        // The id doubles as the collapse key: a notification that is re-planned replaces the one on
        // screen rather than stacking beside it, which is what its id already means.
        data[PushKeys.Collapse] = notification.Id;
        data[At] = notification.At.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (notification.Body is { Length: > 0 } body) data[PushKeys.Body] = body;
        if (notification.Route is { Length: > 0 } route) data[PushKeys.Route] = route;
        if (notification.Channel is { Length: > 0 } channel) data[PushKeys.Channel] = channel;
        if (notification.Category is { Length: > 0 } category) data[PushKeys.Category] = category;
        if (notification.Image is { Length: > 0 } image) data[PushKeys.Image] = image;
        if (notification.Sound is { Length: > 0 } sound) data[Sound] = sound;

        return data;
    }

    /// <summary>Reads a bag written by <see cref="Write"/> back into the app's own keys.</summary>
    internal static Dictionary<string, string>? App(IReadOnlyDictionary<string, string> data)
    {
        var app = data
            .Where(p => !p.Key.StartsWith("spine.", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        return app.Count == 0 ? null : app;
    }
}
