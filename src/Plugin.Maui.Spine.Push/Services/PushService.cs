using AsyncAwaitBestPractices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>
/// The shared half of the client: the installation id, the tags, and the decision of when the
/// backend actually needs to hear from us.
/// </summary>
/// <param name="platform">The platform's answers.</param>
/// <param name="client">The backend.</param>
/// <param name="options">The app's settings.</param>
/// <param name="services">Used to reach <see cref="ILiveActivityService"/> when Widgets is installed.</param>
/// <param name="logger">Where failures are reported.</param>
/// <param name="timeProvider">The clock the daily refresh is measured against.</param>
internal sealed class PushService : IPushService
{
    private readonly IPushPlatform platform;
    private readonly PushRegistrationClient client;
    private readonly SpinePushOptions options;
    private readonly IServiceProvider services;
    private readonly ILogger<PushService> logger;

    internal PushService(
        IPushPlatform platform,
        PushRegistrationClient client,
        SpinePushOptions options,
        IServiceProvider services,
        ILogger<PushService> logger,
        TimeProvider? timeProvider = null)
    {
        this.platform = platform;
        this.client = client;
        this.options = options;
        this.services = services;
        this.logger = logger;
        _time = timeProvider ?? TimeProvider.System;

        // The token does not exist when registration is asked for: on Apple it arrives in a delegate
        // method some time after RegisterForRemoteNotifications returns, and on Android Firebase may
        // rotate it whenever it likes. Without this the first launch never reaches the backend.
        platform.HandleChanged += _ => RefreshAsync().SafeFireAndForget();
    }

    private const string InstallationIdKey = "spine.push.installation-id";
    private const string TagsKey = "spine.push.tags";
    private const string FingerprintKey = "spine.push.fingerprint";
    private const string SentAtKey = "spine.push.sent-at";

    /// <summary>How long a registration may go unsent when nothing has changed.</summary>
    internal static readonly TimeSpan Heartbeat = TimeSpan.FromDays(1);

    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string[] _tags = ReadTags();

    /// <inheritdoc />
    public PushStatus Status => platform.Status;

    /// <inheritdoc />
    public IReadOnlyList<string> Tags => _tags;

    /// <inheritdoc />
    public bool IsRegistered =>
        options.Backend is not null &&
        platform.Status is PushStatus.Authorized or PushStatus.Provisional &&
        platform.Handle is not null &&
        Preferences.Default.Get(FingerprintKey, "").Length > 0;

    /// <inheritdoc />
    public async Task<string> GetInstallationIdAsync()
    {
        if (await SecureStorage.Default.GetAsync(InstallationIdKey) is { Length: > 0 } existing)
            return existing;

        var created = Guid.NewGuid().ToString("N");
        await SecureStorage.Default.SetAsync(InstallationIdKey, created);
        return created;
    }

    /// <inheritdoc />
    public async Task<PushStatus> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        var status = await platform.RequestPermissionAsync(options.Permission, cancellationToken);
        if (status is PushStatus.Authorized or PushStatus.Provisional)
            await RefreshAsync(cancellationToken);

        return status;
    }

    /// <inheritdoc />
    public Task SetTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return ApplyTagsAsync([.. tags.Distinct(StringComparer.Ordinal)], cancellationToken);
    }

    /// <inheritdoc />
    public Task AddTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return ApplyTagsAsync([.. _tags.Concat(tags).Distinct(StringComparer.Ordinal)], cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var removing = tags.ToHashSet(StringComparer.Ordinal);
        return ApplyTagsAsync([.. _tags.Where(t => !removing.Contains(t))], cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (options.Backend is null) return false;
        if (platform.Handle is null) return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var installation = await BuildAsync(cancellationToken);
            var fingerprint = Fingerprint(installation);
            var now = _time.GetUtcNow();

            if (fingerprint == Preferences.Default.Get(FingerprintKey, "") && !IsStale(now))
                return false;

            if (!await client.UpsertAsync(installation, cancellationToken)) return false;

            Preferences.Default.Set(FingerprintKey, fingerprint);
            Preferences.Default.Set(SentAtKey, now.ToUnixTimeSeconds());
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
        if (options.Backend is null) return;

        var id = await GetInstallationIdAsync();
        await client.DeleteAsync(id, cancellationToken);

        Preferences.Default.Remove(FingerprintKey);
        Preferences.Default.Remove(SentAtKey);
    }

    /// <inheritdoc />
    public Task OpenSettingsAsync() => platform.OpenSettingsAsync();

    private async Task ApplyTagsAsync(string[] tags, CancellationToken cancellationToken)
    {
        _tags = tags;
        Preferences.Default.Set(TagsKey, JsonSerializer.Serialize(tags));
        await RefreshAsync(cancellationToken);
    }

    private bool IsStale(DateTimeOffset now)
    {
        var sentAt = Preferences.Default.Get(SentAtKey, 0L);
        return sentAt == 0 || now - DateTimeOffset.FromUnixTimeSeconds(sentAt) >= Heartbeat;
    }

    /// <summary>Everything the backend should know about this device right now.</summary>
    internal async Task<PushInstallation> BuildAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();

        return new PushInstallation
        {
            Id = await GetInstallationIdAsync(),
            Platform = platform.Platform,
            Handle = platform.Handle ?? "",
            Environment = platform.Environment,
            Tags = [.. _tags, .. PlatformTags()],
            AppVersion = AppInfo.Current.VersionString,
            OsVersion = DeviceInfo.Current.VersionString,
            LiveActivities = await LiveActivityTokensAsync(cancellationToken),
            WidgetToken = platform.WidgetToken,
            UpdatedAt = now,
            ExpiresAt = now + options.Expiry,
        };
    }

    /// <summary>The tags Spine sets itself, so a sender can address a platform or an app version.</summary>
    internal IEnumerable<string> PlatformTags() =>
    [
        $"platform:{platform.Platform.ToString().ToLowerInvariant()}",
        $"os:{DeviceInfo.Current.Platform.ToString().ToLowerInvariant()}",
        $"app:{AppInfo.Current.VersionString}",
    ];

    private async Task<LiveActivityTokens?> LiveActivityTokensAsync(CancellationToken cancellationToken)
    {
        // Only present when the app also uses Plugin.Maui.Spine.Widgets; the interface lives in Common,
        // so this package does not have to reference that one.
        if (services.GetService(typeof(ILiveActivityService)) is not ILiveActivityService activities) return null;

        try
        {
            var pushToStart = await activities.GetPushToStartTokenAsync(cancellationToken);
            var running = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var activity in activities.Active)
            {
                if (activity.IsEnded) continue;
                if (await activity.GetPushTokenAsync(cancellationToken) is { Length: > 0 } token)
                    running[activity.Kind] = token;
            }

            return pushToStart is null && running.Count == 0
                ? null
                : new LiveActivityTokens { PushToStart = pushToStart, Activities = running };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogDebug(e, "Spine.Push: could not read the Live Activity tokens.");
            return null;
        }
    }

    /// <summary>
    /// A hash of everything worth telling the backend about. Registration is skipped when it has not
    /// moved, so a foreground does not mean a request. <c>UpdatedAt</c> and <c>ExpiresAt</c> are left
    /// out on purpose — they change on every build and would defeat the whole point.
    /// </summary>
    internal static string Fingerprint(PushInstallation installation)
    {
        var text = string.Join('\n',
            installation.Id,
            installation.Platform,
            installation.Handle,
            installation.Environment,
            string.Join(',', installation.Tags.OrderBy(t => t, StringComparer.Ordinal)),
            installation.AppVersion,
            installation.OsVersion,
            installation.WidgetToken,
            installation.LiveActivities?.PushToStart,
            string.Join(',', (installation.LiveActivities?.Activities ?? new Dictionary<string, string>())
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}")));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string[] ReadTags()
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(Preferences.Default.Get(TagsKey, "[]")) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
