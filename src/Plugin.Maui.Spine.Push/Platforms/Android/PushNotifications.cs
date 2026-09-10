using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using AndroidX.Core.App;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Push.Services;

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

    /// <summary>The button a launch or broadcast intent carries, when it came from one.</summary>
    internal const string ActionExtra = "spine.action";

    /// <summary>The notification a button was on, so it can be taken down once the button is handled.</summary>
    internal const string NotificationIdExtra = "spine.notification-id";

    /// <summary>The <c>RemoteInput</c> key a reply's text is written under.</summary>
    internal const string ReplyKey = "spine.reply";

    private static readonly List<PushChannel> _channels = [];
    private static readonly List<PushCategory> _categories = [];
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
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

            var builder = new NotificationChannelCompat.Builder(channel.Id, importance).SetName(channel.Name);

            if (channel.Sound is { Length: > 0 } sound)
            {
                // Checked here because Android would take the missing file without a word and play the
                // default sound instead — and the channel keeps that choice for good.
                if (context.Resources!.GetIdentifier(sound, "raw", context.PackageName) == 0)
                    Android.Util.Log.Warn("Spine.Push", $"Channel '{channel.Id}': no sound named '{sound}' in Resources/Raw; it gets the system sound.");
                else
                    builder.SetSound(
                        Android.Net.Uri.Parse($"android.resource://{context.PackageName}/raw/{sound}"),
                        new AudioAttributes.Builder()
                            .SetUsage(AudioUsageKind.Notification)!
                            .SetContentType(AudioContentType.Sonification)!
                            .Build());
            }

            manager.CreateNotificationChannel(builder.Build());
        }
    }

    /// <summary>The button sets the app declared, read when a notification that names one is drawn.</summary>
    /// <param name="categories">What the app declared with <c>AddCategory</c>.</param>
    internal static void UseCategories(IReadOnlyList<PushCategory> categories)
    {
        _categories.Clear();
        _categories.AddRange(categories);
    }

    /// <summary>Posts <paramref name="message"/> as a notification.</summary>
    /// <param name="context">The service's context.</param>
    /// <param name="message">What arrived.</param>
    /// <param name="presentation">What the handler asked for; a silent presentation posts without a sound.</param>
    /// <param name="picture">The picture the message named, already fetched; see <see cref="PictureAsync"/>.</param>
    internal static void Show(Context context, PushMessage message, PushPresentation presentation, Bitmap? picture = null)
    {
        var channel = message.Channel is { Length: > 0 } named && _channels.Any(c => c.Id == named)
            ? named
            : _channels.FirstOrDefault().Id ?? DefaultChannelId;

        // A collapse id replaces the notification already on screen instead of stacking another.
        var id = message.CollapseId is { Length: > 0 } collapse
            ? StableId(collapse)
            : Interlocked.Increment(ref _nextId);

        var builder = new NotificationCompat.Builder(context, channel)
            .SetContentTitle(message.Title)
            .SetContentText(message.Body)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(message.Body ?? ""))
            .SetSmallIcon(SmallIcon(context))
            .SetAutoCancel(true)
            .SetContentIntent(OpenIntent(context, message, action: null, id));

        if (picture is not null)
        {
            // The thumbnail beside the text when collapsed, the whole picture when expanded — and no
            // thumbnail repeated next to the full-size one.
            builder.SetLargeIcon(picture)
                .SetStyle(new NotificationCompat.BigPictureStyle().BigPicture(picture).BigLargeIcon((Bitmap?)null));
        }

        if (message.Category is { Length: > 0 } category)
        {
            if (_categories.FirstOrDefault(c => c.Id == category) is { } declared)
            {
                foreach (var action in declared.Actions) builder.AddAction(Button(context, message, action, id));
            }
            else
            {
                Android.Util.Log.Warn("Spine.Push", $"The notification names category '{category}', which the app never declared with AddCategory; it has no buttons.");
            }
        }

        if (!presentation.HasFlag(PushPresentation.Sound))
            builder.SetSilent(true);

        NotificationManagerCompat.From(context).Notify(id, builder.Build());
    }

    /// <summary>
    /// The picture a message names, from an <c>https</c> URL or a file on the device.
    /// <see langword="null"/> when it cannot be had — with the reason in the log — so the notification
    /// still goes out, as text.
    /// </summary>
    /// <param name="message">What arrived.</param>
    /// <param name="cancellationToken">The platform's deadline for the message.</param>
    internal static async Task<Bitmap?> PictureAsync(PushMessage message, CancellationToken cancellationToken)
    {
        if (message.Image is not { Length: > 0 } image) return null;

        try
        {
            if (image.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await _http.GetByteArrayAsync(image, cancellationToken);
                return await BitmapFactory.DecodeByteArrayAsync(bytes, 0, bytes.Length)
                    ?? Nothing($"'{image}' is not a picture Android can decode.");
            }

            if (File.Exists(image))
                return await BitmapFactory.DecodeFileAsync(image) ?? Nothing($"'{image}' is not a picture Android can decode.");

            return Nothing($"'{image}' is neither an https URL nor a file on the device.");
        }
        // Spelled out: Android.OS has an OperationCanceledException of its own.
        catch (Exception e) when (e is not System.OperationCanceledException)
        {
            return Nothing($"fetching '{image}' failed: {e.Message}");
        }

        static Bitmap? Nothing(string why)
        {
            Android.Util.Log.Warn("Spine.Push", $"No picture: {why} The notification is shown without it.");
            return null;
        }
    }

    /// <summary>
    /// One button. One that opens the app goes through the launch intent, like the notification itself;
    /// one that does not goes to <see cref="SpineNotificationActionReceiver"/>, which runs the handler
    /// in the background.
    /// </summary>
    private static NotificationCompat.Action Button(Context context, PushMessage message, PushAction action, int notificationId)
    {
        if (!action.RunsInBackground)
            return new NotificationCompat.Action.Builder(0, action.Title, OpenIntent(context, message, action.Id, notificationId)).Build()!;

        var intent = new Intent(context, typeof(SpineNotificationActionReceiver)).SetAction(SpineNotificationActionReceiver.Action);
        foreach (var (key, value) in message.Data) intent.PutExtra(key, value);
        intent.PutExtra(ActionExtra, action.Id);
        intent.PutExtra(NotificationIdExtra, notificationId);

        // The system writes a reply's text into the intent, which it can only do to a mutable one.
        var flags = action.Reply is null
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : OperatingSystem.IsAndroidVersionAtLeast(31)
                ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable
                : PendingIntentFlags.UpdateCurrent;

        var pending = PendingIntent.GetBroadcast(context, Interlocked.Increment(ref _nextId), intent, flags)!;
        var button = new NotificationCompat.Action.Builder(0, action.Title, pending);

        if (action.Reply is { } placeholder)
            button.AddRemoteInput(new AndroidX.Core.App.RemoteInput.Builder(ReplyKey).SetLabel(placeholder).Build());

        return button.Build()!;
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

    private static PendingIntent? OpenIntent(Context context, PushMessage message, string? action, int notificationId)
    {
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (intent is null) return null;

        intent.AddFlags(ActivityFlags.SingleTop);
        foreach (var (key, value) in message.Data) intent.PutExtra(key, value);

        if (action is not null)
        {
            intent.PutExtra(ActionExtra, action);
            intent.PutExtra(NotificationIdExtra, notificationId);
        }

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
            // The button travels beside the message, not in it.
            if (key == ActionExtra) continue;
            if (extras.GetString(key) is { } value) data[key] = value;
        }

        return data.Count == 0 ? null : PushMessage.From(data);
    }
}
