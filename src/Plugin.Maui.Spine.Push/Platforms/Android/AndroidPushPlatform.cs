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
