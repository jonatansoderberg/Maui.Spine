namespace Plugin.Maui.Spine.Biometrics;

/// <summary>
/// The platform's own biometric prompt: Face ID, Touch ID or Optic ID on Apple platforms, the fingerprint or
/// face prompt on Android, Windows Hello on Windows. Each falls back to the device passcode unless
/// <see cref="BiometricOptions.AllowDeviceCredential"/> is false.
/// </summary>
/// <remarks>
/// Most pages need <see cref="IUnlock"/> or <see cref="RequiresUnlockAttribute"/> instead, which remember a
/// recent unlock and show the messages. An unlock protects the user interface, not data at rest.
/// </remarks>
public interface IBiometricAuth
{
    /// <summary>What the device offers right now.</summary>
    Task<BiometricAvailability> GetAvailabilityAsync();

    /// <summary>
    /// Shows the platform's prompt with <paramref name="reason"/>, which the user reads as why the app asks.
    /// Completes with the outcome; it never throws for a refusal.
    /// </summary>
    Task<BiometricResult> AuthenticateAsync(string reason, BiometricOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>What a device can authenticate with.</summary>
/// <param name="Kind">The biometric the device has, or <see cref="BiometricKind.None"/>.</param>
/// <param name="Enrolled">Whether a face or a finger is enrolled for <paramref name="Kind"/>.</param>
/// <param name="HasDeviceCredential">Whether a passcode, PIN, pattern or password is set.</param>
public sealed record BiometricAvailability(BiometricKind Kind, bool Enrolled, bool HasDeviceCredential)
{
    /// <summary>Whether a prompt can succeed: a biometric is enrolled, or a device credential can stand in.</summary>
    public bool CanAuthenticate => Enrolled || HasDeviceCredential;
}

/// <summary>The biometric a device has.</summary>
public enum BiometricKind
{
    /// <summary>No biometric hardware, or none the app may use.</summary>
    None,

    /// <summary>Face ID (iPhone, iPad).</summary>
    FaceId,

    /// <summary>Touch ID (iPhone, iPad, Mac).</summary>
    TouchId,

    /// <summary>Optic ID (Apple Vision Pro).</summary>
    OpticId,

    /// <summary>Android's fingerprint or face unlock; Android does not say which the user enrolled.</summary>
    Biometric,

    /// <summary>Windows Hello: face, fingerprint or PIN.</summary>
    WindowsHello,
}

/// <summary>How a prompt ended.</summary>
public enum BiometricResult
{
    /// <summary>The user was recognised, or entered the device credential.</summary>
    Success,

    /// <summary>The user, the system or the app cancelled the prompt.</summary>
    Canceled,

    /// <summary>The user was not recognised.</summary>
    Failed,

    /// <summary>Too many failed attempts; the device credential is needed before biometrics work again.</summary>
    LockedOut,

    /// <summary>The device has biometrics but nothing is enrolled, and no device credential may stand in.</summary>
    NotEnrolled,

    /// <summary>Nothing to authenticate with: no hardware, no passcode, a policy, or a missing Info.plist key.</summary>
    NotAvailable,
}

/// <summary>Options for one prompt.</summary>
public sealed class BiometricOptions
{
    /// <summary>Whether the device passcode, PIN, pattern or password may be used instead of a biometric. Defaults to true.</summary>
    public bool AllowDeviceCredential { get; init; } = true;

    /// <summary>The prompt's title on Android; iOS and Windows show the reason only.</summary>
    public string? Title { get; init; }
}
