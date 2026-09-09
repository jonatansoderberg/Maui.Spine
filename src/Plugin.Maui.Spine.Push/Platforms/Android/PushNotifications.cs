using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Push;

/// <summary>
/// Draws the notification Spine's data-only messages carry. Doing it here rather than letting FCM's
/// <c>notification</c> block do it is what makes foreground and background behave alike and lets the
/// app pick the channel (§5.2).
/// </summary>
internal static class PushNotifications
{
    /// <summary>The channel a message that names none is posted to.</summary>
    internal const string DefaultChannelId = "spine.push.default";

    private static readonly List<PushChannel> _channels = [];
    private static int _nextId = 1;

    /// <summary>Creates the app's channels. Android ignores a channel that already exists.</summary>
    /// <param name="context">Any context.</param>
    /// <param name="channels">The channels the app configured; a default one is added when it configured none.</param>
    internal static void CreateChannels(Context context, IReadOnlyList<PushChannel> channels)
    {
        _channels.Clear();
        _channels.AddRange(channels.Count > 0 ? channels : [new PushChannel(DefaultChannelId, "Notifications")]);

        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var manager = NotificationManagerCompat.From(context);

        foreach (var channel in _channels)
        {
            var importance = channel.Importance switch
            {
                PushChannelImportance.Low => NotificationManagerCompat.ImportanceLow,
                PushChannelImportance.High => NotificationManagerCompat.ImportanceHigh,
                _ => NotificationManagerCompat.ImportanceDefault,
            };

            manager.CreateNotificationChannel(
                new NotificationChannelCompat.Builder(channel.Id, importance).SetName(channel.Name).Build());
        }
    }

    /// <summary>Posts <paramref name="message"/> as a notification.</summary>
    /// <param name="context">The service's context.</param>
    /// <param name="message">What arrived.</param>
    /// <param name="presentation">What the handler asked for; a silent presentation posts without a sound.</param>
    internal static void Show(Context context, PushMessage message, PushPresentation presentation)
    {
        var channel = message.Channel is { Length: > 0 } named && _channels.Any(c => c.Id == named)
            ? named
            : _channels.FirstOrDefault().Id ?? DefaultChannelId;

        var builder = new NotificationCompat.Builder(context, channel)
            .SetContentTitle(message.Title)
            .SetContentText(message.Body)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(message.Body ?? ""))
            .SetSmallIcon(SmallIcon(context))
            .SetAutoCancel(true)
            .SetContentIntent(OpenIntent(context, message));

        if (!presentation.HasFlag(PushPresentation.Sound))
            builder.SetSilent(true);

        // A collapse id replaces the notification already on screen instead of stacking another.
        var id = message.CollapseId is { Length: > 0 } collapse
            ? StableId(collapse)
            : Interlocked.Increment(ref _nextId);

        NotificationManagerCompat.From(context).Notify(id, builder.Build());
    }

    /// <summary>
    /// A hash of <paramref name="value"/> that means the same thing in every process (FNV-1a).
    /// </summary>
    /// <remarks>
    /// <see cref="string.GetHashCode()"/> is salted per run, so an id derived from it does not match
    /// the one an earlier run of the app computed — and both a collapse id and a scheduled alarm's
    /// request code are only worth anything when they do.
    /// </remarks>
    /// <param name="value">The id to hash.</param>
    /// <returns>A non-negative id.</returns>
    internal static int StableId(string value)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return (int)(hash & int.MaxValue);
        }
    }

    private static int SmallIcon(Context context)
    {
        // The app can override the icon by adding a drawable named spine_push_icon; otherwise the
        // launcher icon is used, which is what the platform falls back to anyway.
        var resources = context.Resources!;
        var custom = resources.GetIdentifier("spine_push_icon", "drawable", context.PackageName);
        return custom != 0 ? custom : context.ApplicationInfo!.Icon;
    }

    private static PendingIntent? OpenIntent(Context context, PushMessage message)
    {
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (intent is null) return null;

        intent.AddFlags(ActivityFlags.SingleTop);
        foreach (var (key, value) in message.Data) intent.PutExtra(key, value);

        var flags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
        return PendingIntent.GetActivity(context, Interlocked.Increment(ref _nextId), intent, flags);
    }

    /// <summary>Reads a launch intent's extras back into a message, when the app was opened from a notification.</summary>
    /// <param name="intent">The intent the activity was started or resumed with.</param>
    /// <returns>The message, or <see langword="null"/> when the intent did not come from Spine.</returns>
    internal static PushMessage? Read(Intent? intent)
    {
        if (intent?.Extras is not { } extras) return null;
        if (!extras.ContainsKey(PushKeys.Kind)) return null;

        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in extras.KeySet() ?? [])
        {
            if (extras.GetString(key) is { } value) data[key] = value;
        }

        return data.Count == 0 ? null : PushMessage.From(data);
    }
}
