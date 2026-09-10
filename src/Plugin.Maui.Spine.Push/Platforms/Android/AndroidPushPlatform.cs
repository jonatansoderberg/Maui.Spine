using Android.Content;
using Android.Gms.Extensions;
using AndroidX.Core.App;
using Firebase.Messaging;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Push.Services;

namespace Plugin.Maui.Spine.Push;

/// <summary>The Android half of <see cref="IPushService"/>.</summary>
internal sealed class AndroidPushPlatform : IPushPlatform
{
    private static AndroidPushPlatform? _current;
    private static string? _handle;

    internal AndroidPushPlatform() => _current = this;

    /// <inheritdoc />
    public Common.PushPlatform Platform => Common.PushPlatform.Android;

    /// <inheritdoc />
    public string? Handle => _handle;

    /// <inheritdoc />
    public ApnsEnvironment? Environment => null;


    /// <inheritdoc />
    public event Action<string>? HandleChanged;

    /// <summary>
    /// Whether the app may post notifications. Android has no "provisional", and a user who turned
    /// notifications off in settings looks the same as one who declined — both are
    /// <see cref="PushStatus.Denied"/>, which is what the app needs to act on either way.
    /// </summary>
    public PushStatus Status =>
        NotificationManagerCompat.From(Android.App.Application.Context).AreNotificationsEnabled()
            ? PushStatus.Authorized
            : PushStatus.Denied;

    /// <inheritdoc />
    public async Task<PushStatus> RequestPermissionAsync(PushPermission permission, CancellationToken cancellationToken)
    {
        // POST_NOTIFICATIONS only exists from API 33; below that the permission is granted at install.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            await Permissions.RequestAsync<Permissions.PostNotifications>();

        await FetchTokenAsync();
        return Status;
    }

    /// <inheritdoc />
    public Task OpenSettingsAsync() =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            var context = Android.App.Application.Context;
            using var intent = new Intent(Android.Provider.Settings.ActionAppNotificationSettings);
            intent.PutExtra(Android.Provider.Settings.ExtraAppPackage, context.PackageName);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        });

    /// <summary>The FCM topics subscribed to for broadcast channels, kept so a later call knows what to leave.</summary>
    private const string TopicsKey = "spine.push.channel-topics";

    /// <inheritdoc />
    public async Task FollowChannelsAsync(IReadOnlySet<string> channels)
    {
        var wanted = channels.Select(LiveActivityChannels.Topic).ToHashSet();
        var followed = Preferences.Default.Get(TopicsKey, "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (wanted.SetEquals(followed)) return;

        // Each change is written down as soon as Firebase confirms it, so one that fails is tried
        // again at the next call instead of being forgotten.
        foreach (var topic in wanted.Except(followed))
        {
            if (await TopicAsync(topic, subscribe: true)) followed.Add(topic);
        }

        foreach (var topic in followed.Except(wanted).ToList())
        {
            if (await TopicAsync(topic, subscribe: false)) followed.Remove(topic);
        }

        Preferences.Default.Set(TopicsKey, string.Join(' ', followed));
    }

    private static async Task<bool> TopicAsync(string topic, bool subscribe)
    {
        try
        {
            var task = subscribe
                ? FirebaseMessaging.Instance.SubscribeToTopic(topic)
                : FirebaseMessaging.Instance.UnsubscribeFromTopic(topic);
            await task.AsAsync<Java.Lang.Object>();
            Logger?.LogInformation("Spine.Push: {Action} FCM topic {Topic}.", subscribe ? "subscribed to" : "unsubscribed from", topic);
            return true;
        }
        catch (Exception e)
        {
            Logger?.LogWarning(e, "Spine.Push: could not {Action} FCM topic {Topic}.", subscribe ? "subscribe to" : "unsubscribe from", topic);
            return false;
        }
    }

    /// <summary>Asks Firebase for the registration token and remembers it.</summary>
    internal static async Task FetchTokenAsync()
    {
        try
        {
            // GetToken returns a Google Play services Task, not a .NET one.
            var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.Object>();
            if (token?.ToString() is { Length: > 0 } value) SetHandle(value);
        }
        catch (Exception e)
        {
            Logger?.LogWarning(e, "Spine.Push: could not read the FCM registration token.");
        }
    }

    /// <summary>Called by the messaging service when Firebase rotates the token.</summary>
    internal static void SetHandle(string token)
    {
        if (token == _handle) return;
        _handle = token;
        _current?.HandleChanged?.Invoke(token);
    }

    private static ILogger? Logger =>
        IPlatformApplication.Current?.Services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Push");
}
