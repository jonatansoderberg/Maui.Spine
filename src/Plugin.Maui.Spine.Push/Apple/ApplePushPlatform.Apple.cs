#if IOS || MACCATALYST
using AsyncAwaitBestPractices;
using Foundation;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Push.Extensions;
using Plugin.Maui.Spine.Push.Services;
using UIKit;
using UserNotifications;

namespace Plugin.Maui.Spine.Push;

/// <summary>The Apple half of <see cref="IPushService"/>.</summary>
internal sealed class ApplePushPlatform : IPushPlatform
{
    /// <summary>iOS follows a channel itself, from the moment the activity is started on it.</summary>
    public Task FollowChannelsAsync(IReadOnlySet<string> channels) => Task.CompletedTask;

    // Static because the delegate methods can fire before the MAUI app exists — a cold start from a
    // notification reaches didReceiveRemoteNotification: before CreateMauiApp has returned.
    private static string? _handle;
    private static readonly List<(NSDictionary Payload, Action<UIBackgroundFetchResult> Done)> _pending = [];

    /// <inheritdoc />
    public Common.PushPlatform Platform => Common.PushPlatform.Apple;

    /// <inheritdoc />
    public string? Handle => _handle;

    /// <inheritdoc />
    public ApnsEnvironment? Environment { get; } = ReadEnvironment();


    /// <inheritdoc />
    public PushStatus Status { get; private set; } = PushStatus.NotDetermined;

    /// <inheritdoc />
    public event Action<string>? HandleChanged;

    private static ApplePushPlatform? _current;

    private readonly SpinePushOptions _options;

    internal ApplePushPlatform(SpinePushOptions options)
    {
        _options = options;
        _current = this;
    }

    /// <inheritdoc />
    public async Task<PushStatus> RequestPermissionAsync(PushPermission permission, CancellationToken cancellationToken)
    {
        var options = UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge;
        if (permission == PushPermission.Provisional) options |= UNAuthorizationOptions.Provisional;

        var (granted, error) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(options);
        if (error is not null)
            Logger?.LogWarning("Spine.Push: requesting authorization failed: {Error}", error.LocalizedDescription);

        await RefreshStatusAsync();

        // Without a backend, or without push in the build, there is nothing APNs could give a token
        // for: the app only notifies locally, and asking would earn a failure in the log.
        if (granted && _options.Backend is not null && IsRemoteConfigured)
            await MainThread.InvokeOnMainThreadAsync(UIApplication.SharedApplication.RegisterForRemoteNotifications);

        return Status;
    }

    /// <inheritdoc />
    public Task OpenSettingsAsync() =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            using var url = new NSUrl(UIApplication.OpenSettingsUrlString);
            UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);
        });

    /// <summary>Asks the system what the user has decided, and remembers it.</summary>
    internal async Task RefreshStatusAsync()
    {
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();

        Status = settings.AuthorizationStatus switch
        {
            UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Ephemeral => PushStatus.Authorized,
            UNAuthorizationStatus.Provisional => PushStatus.Provisional,
            UNAuthorizationStatus.Denied => PushStatus.Denied,
            _ => PushStatus.NotDetermined,
        };
    }

    internal static void DidRegister(NSData? deviceToken)
    {
        if (deviceToken is null) return;

        var bytes = new byte[deviceToken.Length];
        System.Runtime.InteropServices.Marshal.Copy(deviceToken.Bytes, bytes, 0, (int)deviceToken.Length);
        var token = Convert.ToHexStringLower(bytes);

        if (token == _handle) return;
        _handle = token;

        _current?.HandleChanged?.Invoke(token);
    }

    internal static void DidFailToRegister(NSError? error) =>
        Logger?.LogWarning("Spine.Push: APNs registration failed: {Error}", error?.LocalizedDescription ?? "unknown");

    internal static void DidReceive(NSDictionary? userInfo, Action<UIBackgroundFetchResult> completionHandler)
    {
        if (userInfo is null) { completionHandler(UIBackgroundFetchResult.NoData); return; }

        if (IPlatformApplication.Current?.Services is null)
        {
            // Cold start from a push: hold it until the app has a service provider.
            lock (_pending) _pending.Add((userInfo, completionHandler));
            return;
        }

        HandleAsync(userInfo, completionHandler, isColdStart: false).SafeFireAndForget();
    }

    /// <summary>Delivers whatever arrived before the app had started. Called once the host exists.</summary>
    internal static void DrainPending()
    {
        (NSDictionary, Action<UIBackgroundFetchResult>)[] waiting;
        lock (_pending)
        {
            if (_pending.Count == 0) return;
            waiting = [.. _pending.Select(p => (p.Payload, p.Done))];
            _pending.Clear();
        }

        foreach (var (payload, done) in waiting)
            HandleAsync(payload, done, isColdStart: true).SafeFireAndForget();
    }

    private static async Task HandleAsync(
        NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler, bool isColdStart)
    {
        // The platform gives the app about thirty seconds; stop a little short of it so the completion
        // handler is always called, which is what keeps the OS willing to deliver the next one.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        try
        {
            var message = PushPayload.Read(userInfo);
            var context = new PushContext(
                IsForeground: UIApplication.SharedApplication.ApplicationState == UIApplicationState.Active,
                IsColdStart: isColdStart,
                ReceivedAt: DateTimeOffset.UtcNow,
                Deadline: deadline.Token);

            var services = SpinePushExtensions.Services();

            // A silent push is how a widget rebuild reaches iOS until the dedicated widgets push type.
            await SpinePushExtensions.HandleInternallyAsync(services, message, deadline.Token);
            await SpinePushExtensions.DeliverAsync(services, message, context);
            completionHandler(UIBackgroundFetchResult.NewData);
        }
        catch (Exception e)
        {
            Logger?.LogError(e, "Spine.Push: handling a remote notification failed.");
            completionHandler(UIBackgroundFetchResult.Failed);
        }
    }

    /// <summary>
    /// Which APNs host the token belongs to, read from the entitlement the build wrote. A token from
    /// a development build only works against the sandbox, so the server has to be told.
    /// </summary>
    /// <summary>
    /// Whether the build set the app up for remote push. The targets write <c>SpinePushEnvironment</c>
    /// into Info.plist exactly when they add the push entitlement, so it is absent with
    /// <c>SpinePushRemote=false</c> — and on Mac Catalyst without a provisioning profile, where the
    /// entitlement cannot be used and an app signed with it would not launch.
    /// </summary>
    internal static bool IsRemoteConfigured { get; } =
        NSBundle.MainBundle.ObjectForInfoDictionary("SpinePushEnvironment") is not null;

    private static ApnsEnvironment ReadEnvironment() =>
        NSBundle.MainBundle.ObjectForInfoDictionary("SpinePushEnvironment")?.ToString() switch
        {
            "development" => ApnsEnvironment.Sandbox,
            "production" => ApnsEnvironment.Production,
            _ => IsDebuggerAttachedOrDevelopmentBuild() ? ApnsEnvironment.Sandbox : ApnsEnvironment.Production,
        };

    private static bool IsDebuggerAttachedOrDevelopmentBuild() =>
#if DEBUG
        true;
#else
        false;
#endif

    private static ILogger? Logger =>
        IPlatformApplication.Current?.Services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Push");
}
#endif
