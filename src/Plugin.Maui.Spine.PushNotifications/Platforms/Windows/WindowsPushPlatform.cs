using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.PushNotifications;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.PushNotifications.Extensions;
using Plugin.Maui.Spine.PushNotifications.Services;
using static Plugin.Maui.Spine.PushNotifications.WindowsLog;

namespace Plugin.Maui.Spine.PushNotifications;

/// <summary>
/// The Windows half of <see cref="IPushNotificationService"/>: a WNS channel from the Windows App SDK's
/// <c>PushNotificationManager</c>, whose URI is the handle. Alerts are toasts WNS draws itself; what
/// reaches the app is raw notifications while it runs, and a tapped toast.
/// </summary>
internal sealed class WindowsPushPlatform(SpinePushNotificationsOptions options) : IPushPlatform
{
    /// <summary>A channel is good for 30 days; one closer than this to running out is replaced on the next foreground.</summary>
    private static readonly TimeSpan RenewBefore = TimeSpan.FromDays(1);

    /// <summary>How long a permission request waits for the channel before answering without it; it registers when it comes.</summary>
    private static readonly TimeSpan ChannelWait = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private Task _channel = Task.CompletedTask;
    private readonly Lazy<string?> _unsupported = new(() => FindUnsupported(options));
    private string? _handle;
    private DateTimeOffset _expiresAt;
    private volatile bool _registered;

    /// <inheritdoc />
    public Common.PushPlatform Platform => Common.PushPlatform.Windows;

    /// <inheritdoc />
    public string? Handle => _handle;

    /// <inheritdoc />
    public ApnsEnvironment? Environment => null;

    /// <inheritdoc />
    public event Action<string>? HandleChanged;

    /// <summary>Whether a window of the app is active, which a raw push's <see cref="PushContext"/> reports.</summary>
    internal bool IsForeground { get; set; }

    /// <summary>
    /// Whether <see cref="Register"/> has run. The Windows App SDK wants <c>PushNotificationManager.Register()</c>
    /// before the first channel request, and MAUI can show a page — and activate its window — before the
    /// OnLaunched event that registers.
    /// </summary>
    internal bool IsRegistered => _registered;

    /// <summary>Why this app cannot get push on this machine, or <see langword="null"/> when it can.</summary>
    internal string? Unsupported => _unsupported.Value;

    /// <summary>
    /// <see cref="PushStatus.Unsupported"/> for the reasons in <see cref="Unsupported"/>. Otherwise what the
    /// user decided in Settings: Windows never asks, so there is no <see cref="PushStatus.NotDetermined"/>.
    /// </summary>
    public PushStatus Status
    {
        get
        {
            if (Unsupported is not null) return PushStatus.Unsupported;

            try
            {
                return AppNotificationManager.Default.Setting switch
                {
                    AppNotificationSetting.Enabled => PushStatus.Authorized,
                    AppNotificationSetting.Unsupported => PushStatus.Unsupported,
                    _ => PushStatus.Denied,
                };
            }
            catch (Exception e)
            {
                Logger?.LogWarning(e, "Spine.PushNotifications: reading AppNotificationManager.Setting failed (HRESULT 0x{HResult:X8}).", e.HResult);
                return PushStatus.Denied;
            }
        }
    }

    /// <summary>
    /// Windows has no prompt; this answers with the setting. It starts a channel request if there is no
    /// channel and waits a little for it, so the usual case registers in the same call. WNS can take up to
    /// 15 minutes when it has to retry; the request carries on and registers through
    /// <see cref="HandleChanged"/> when it finishes.
    /// </summary>
    public async Task<PushStatus> RequestPermissionAsync(PushPermission permission, CancellationToken cancellationToken)
    {
        try
        {
            await ChannelAsync().WaitAsync(ChannelWait, cancellationToken);
        }
        catch (TimeoutException)
        {
            Logger?.LogInformation("Spine.PushNotifications: no WNS channel yet after {Seconds} s; the app registers when it arrives.", ChannelWait.TotalSeconds);
        }

        return Status;
    }

    /// <inheritdoc />
    public Task OpenSettingsAsync() =>
        Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(new Uri("ms-settings:notifications"));

    /// <summary>Windows has no Live Activities, so no broadcast channels to follow.</summary>
    public Task FollowChannelsAsync(IReadOnlySet<string> channels) => Task.CompletedTask;

    /// <summary>
    /// Subscribes to raw pushes and registers with the Windows App SDK. The handler goes first: the SDK
    /// throws "Must register event handlers before calling Register()" otherwise.
    /// </summary>
    internal void Register()
    {
        if (Unsupported is { } reason)
        {
            // An app with no backend only notifies locally and did not ask for push; it gets no warning for it.
            if (options.Backend is not null)
                Logger?.LogWarning("Spine.PushNotifications: no push on this Windows installation: {Reason}", reason);
            return;
        }

        try
        {
            PushNotificationManager.Default.PushReceived += (_, args) => Receive(args, isColdStart: false);
            PushNotificationManager.Default.Register();
        }
        catch (Exception e)
        {
            Logger?.LogError(e, "Spine.PushNotifications: PushNotificationManager.Register() failed (HRESULT 0x{HResult:X8}); this app gets no raw pushes.", e.HResult);
        }
        finally
        {
            // A failed Register() still lets the channel request run, which logs its own reason.
            _registered = true;
        }
    }

    /// <summary>
    /// Asks WNS for a channel and makes its URI the handle, unless this process already holds one that is
    /// good for another day. The handle is not kept across launches, so every launch asks afresh — as
    /// Microsoft recommends, since the URI can change. Nothing before <see cref="Register"/>: the launch
    /// asks once it has registered. Never throws; failures are logged.
    /// </summary>
    /// <remarks>
    /// One request at a time, and every caller while it runs gets that same request: the platform retries
    /// on its own for up to 15 minutes, and a caller should not queue behind it for another 15.
    /// </remarks>
    internal Task ChannelAsync()
    {
        if (Unsupported is not null || options.Backend is null || !_registered || IsCurrent()) return Task.CompletedTask;

        lock (_gate)
        {
            if (!_channel.IsCompleted) return _channel;
            if (IsCurrent()) return Task.CompletedTask;

            return _channel = RequestChannelAsync();
        }
    }

    private async Task RequestChannelAsync()
    {
        try
        {
            var remoteId = options.Windows.RemoteId!.Value;
            var operation = PushNotificationManager.Default.CreateChannelAsync(remoteId);

            // The platform retries on its own for up to 15 minutes; each retry is worth seeing in the log.
            operation.Progress = (_, progress) =>
            {
                if (progress.status == PushNotificationChannelStatus.InProgressRetry)
                    Logger?.LogInformation("Spine.PushNotifications: WNS channel request retrying (attempt {Attempt}, HRESULT 0x{HResult:X8}).",
                        progress.retryCount, progress.extendedError?.HResult ?? 0);
            };

            var result = await operation;

            if (result.Status != PushNotificationChannelStatus.CompletedSuccess || result.Channel is not { } channel)
            {
                Logger?.LogWarning(
                    "Spine.PushNotifications: WNS gave no channel ({Status}, HRESULT 0x{HResult:X8}: {Error}). Check that Windows.RemoteId ({RemoteId}) " +
                    "is the Object ID of the Entra app's service principal (Managed application in local directory), that the registration is multitenant, " +
                    "and for a packaged app that Microsoft has mapped its package family name.",
                    result.Status, result.ExtendedError?.HResult ?? 0, result.ExtendedError?.Message, remoteId);
                return;
            }

            _expiresAt = channel.ExpirationTime;

            // OriginalString: the URI carries an escaped token that Uri.ToString() would unescape.
            var uri = channel.Uri.OriginalString;
            if (uri == _handle) return;

            _handle = uri;
            HandleChanged?.Invoke(uri);
        }
        catch (Exception e)
        {
            Logger?.LogWarning(e, "Spine.PushNotifications: requesting a WNS channel failed (HRESULT 0x{HResult:X8}).", e.HResult);
        }
    }

    /// <summary>
    /// A raw push: while the app runs through <c>PushReceived</c>, and in a packaged app started for it
    /// through the activation arguments. Held with a deferral until the handler is done.
    /// </summary>
    internal void Receive(PushNotificationReceivedEventArgs args, bool isColdStart)
    {
        var deferral = args.GetDeferral();
        HandleAsync(args.Payload, isColdStart).SafeFireAndForget();

        async Task HandleAsync(byte[] payload, bool coldStart)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));

            try
            {
                var message = PushMessage.From(WnsPayload.ReadRaw(payload));
                var context = new PushContext(IsForeground, coldStart, DateTimeOffset.UtcNow, deadline.Token);
                var services = SpinePushNotificationsExtensions.Services();

                await SpinePushNotificationsExtensions.HandleInternallyAsync(services, message, deadline.Token);
                var presentation = await SpinePushNotificationsExtensions.DeliverAsync(services, message, context);

                // WNS draws the server's alerts itself. One sent raw by hand is drawn here, as Android does.
                if (message.Kind == PushKind.Alert && (message.Title is not null || message.Body is not null) &&
                    !(IsForeground && presentation == PushPresentation.None))
                {
                    WindowsNotifications.Show(message, options);
                }
            }
            catch (Exception e)
            {
                Logger?.LogError(e, "Spine.PushNotifications: handling a WNS raw notification failed.");
            }
            finally
            {
                deferral.Complete();
            }
        }
    }

    private bool IsCurrent() => _handle is not null && _expiresAt - DateTimeOffset.UtcNow > RenewBefore;

    private static string? FindUnsupported(SpinePushNotificationsOptions options)
    {
        if (options.Windows.RemoteId is null)
            return "SpinePushNotificationsOptions.Windows.RemoteId is not set. It is the Object ID of the Entra app's service principal; see the wiki's Windows section.";

        if (System.Environment.IsPrivilegedProcess)
            return "the app runs elevated (as administrator), and the Windows App SDK does not deliver push to elevated processes.";

        try
        {
            return PushNotificationManager.IsSupported()
                ? null
                : "PushNotificationManager.IsSupported() is false. The usual cause is a self-contained Windows App SDK — WindowsAppSDKSelfContained=true, " +
                  ".NET MAUI's default for WindowsPackageType=None. Set WindowsAppSDKSelfContained=false so the app uses the installed Windows App Runtime.";
        }
        catch (Exception e)
        {
            return $"PushNotificationManager.IsSupported() threw {e.GetType().Name} (HRESULT 0x{e.HResult:X8}): {e.Message}";
        }
    }
}
