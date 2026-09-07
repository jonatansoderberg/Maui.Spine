using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>The push service an installation is reached through.</summary>
public enum PushPlatform
{
    /// <summary>Apple Push Notification service, for iOS and Mac Catalyst.</summary>
    Apple,

    /// <summary>Firebase Cloud Messaging, for Android.</summary>
    Android,

    /// <summary>Windows Notification Service.</summary>
    Windows,
}

/// <summary>Which APNs host a device token belongs to.</summary>
/// <remarks>
/// A token issued to a development build is only valid against the sandbox host and vice versa, so
/// the app reports which one it got. <see cref="PerInstallation"/> is not a value an installation
/// carries — it is the server setting that says to trust what each installation reported.
/// </remarks>
public enum ApnsEnvironment
{
    /// <summary>api.sandbox.push.apple.com — development builds.</summary>
    Sandbox,

    /// <summary>api.push.apple.com — TestFlight and App Store builds.</summary>
    Production,

    /// <summary>Server setting only: use the environment each installation reported.</summary>
    PerInstallation,
}

/// <summary>
/// The push tokens a device holds for Live Activities. They rotate, so the app sends them at
/// launch, on foreground, and whenever the platform reports a new one.
/// </summary>
public sealed record LiveActivityTokens
{
    /// <summary>The push-to-start token, which starts an activity that is not running yet (iOS 17.2+).</summary>
    public string? PushToStart { get; init; }

    /// <summary>Tokens for the activities that are running, keyed by the kind each was started with.</summary>
    public IReadOnlyDictionary<string, string> Activities { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// One app installation on one device, as the server's register knows it. Shaped like Azure
/// Notification Hubs' <c>Installation</c> on purpose, so a hub-backed transport could be dropped in
/// later without the app noticing.
/// </summary>
public sealed record PushInstallation
{
    /// <summary>The app's own id for this installation; stable across launches and token changes.</summary>
    public required string Id { get; init; }

    /// <summary>The service this installation is reached through.</summary>
    public required PushPlatform Platform { get; init; }

    /// <summary>The device token, FCM registration token, or WNS channel URI.</summary>
    public required string Handle { get; init; }

    /// <summary>
    /// Which APNs host <see cref="Handle"/> is valid against. Apple only, and never
    /// <see cref="ApnsEnvironment.PerInstallation"/>.
    /// </summary>
    public ApnsEnvironment? Environment { get; init; }

    /// <summary>What this installation subscribes to. Matched by <see cref="PushTagExpression"/>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The signed-in user, when there is one. Conventionally also present as the tag <c>user:&lt;id&gt;</c>.</summary>
    public string? UserId { get; init; }

    /// <summary>The app version that registered, for diagnosing payloads an old build cannot read.</summary>
    public string? AppVersion { get; init; }

    /// <summary>The operating system version that registered.</summary>
    public string? OsVersion { get; init; }

    /// <summary>The device's Live Activity tokens, when the app has any.</summary>
    public LiveActivityTokens? LiveActivities { get; init; }

    /// <summary>The widget extension's push token (iOS 26+), when the app has one.</summary>
    public string? WidgetToken { get; init; }

    /// <summary>When this registration was last written. Used by <c>Prune</c> to drop stale devices.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the registration should be considered dead even if nothing invalidated it.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Whether <see cref="ExpiresAt"/> has passed at <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } e && e <= now;
}

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PushInstallation))]
[JsonSerializable(typeof(LiveActivityTokens))]
internal sealed partial class PushJsonContext : JsonSerializerContext;

/// <summary>The wire format for a <see cref="PushInstallation"/>, shared by the app and the server.</summary>
public static class PushJson
{
    /// <summary>Serializes <paramref name="installation"/> as the body of a registration request.</summary>
    public static string Serialize(PushInstallation installation) =>
        System.Text.Json.JsonSerializer.Serialize(installation, PushJsonContext.Default.PushInstallation);

    /// <summary>Reads a registration request body, or <see langword="null"/> when it is not one.</summary>
    public static PushInstallation? Deserialize(string json) =>
        System.Text.Json.JsonSerializer.Deserialize(json, PushJsonContext.Default.PushInstallation);
}
