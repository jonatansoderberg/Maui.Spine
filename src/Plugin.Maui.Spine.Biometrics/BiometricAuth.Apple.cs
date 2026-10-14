#if IOS || MACCATALYST
using Foundation;
using LocalAuthentication;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary><see cref="IBiometricAuth"/> on LocalAuthentication: Face ID, Touch ID or Optic ID, with the passcode to fall back on.</summary>
internal sealed class BiometricAuth : IBiometricAuth
{
    public Task<BiometricAvailability> GetAvailabilityAsync()
    {
        using var context = new LAContext();
        var enrolled = context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out _);
        var kind = Kind(context.BiometryType);
        var any = context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _);

        return Task.FromResult(new BiometricAvailability(kind, enrolled, HasDeviceCredential: any));
    }

    public async Task<BiometricResult> AuthenticateAsync(string reason, BiometricOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new BiometricOptions();
        using var context = new LAContext { LocalizedCancelTitle = BiometricsStrings.Get("Spine.Biometrics.Cancel") };
        var policy = options.AllowDeviceCredential ? LAPolicy.DeviceOwnerAuthentication : LAPolicy.DeviceOwnerAuthenticationWithBiometrics;

        if (!context.CanEvaluatePolicy(policy, out var unavailable))
            return Map(unavailable);

        // Evaluating Face ID without the usage text ends the app; refuse with a reason instead.
        if (context.BiometryType == LABiometryType.FaceId && NSBundle.MainBundle.ObjectForInfoDictionary("NSFaceIDUsageDescription") is null)
        {
            System.Diagnostics.Debug.WriteLine("[Spine] Biometrics: Face ID needs NSFaceIDUsageDescription in Info.plist; the unlock is refused.");
            return BiometricResult.NotAvailable;
        }

        using var registration = cancellationToken.Register(context.Invalidate);
        var (success, error) = await context.EvaluatePolicyAsync(policy, reason);

        return success ? BiometricResult.Success : Map(error);
    }

    private static BiometricKind Kind(LABiometryType type)
    {
        if ((OperatingSystem.IsIOSVersionAtLeast(17) || OperatingSystem.IsMacCatalystVersionAtLeast(17)) && type == LABiometryType.OpticId)
            return BiometricKind.OpticId;

        return type switch
        {
            LABiometryType.FaceId => BiometricKind.FaceId,
            LABiometryType.TouchId => BiometricKind.TouchId,
            _ => BiometricKind.None,
        };
    }

    private static BiometricResult Map(NSError? error) => error is null ? BiometricResult.Failed : (LAStatus)(long)error.Code switch
    {
        LAStatus.UserCancel or LAStatus.SystemCancel or LAStatus.AppCancel or LAStatus.UserFallback => BiometricResult.Canceled,
        LAStatus.BiometryLockout => BiometricResult.LockedOut,
        LAStatus.BiometryNotEnrolled => BiometricResult.NotEnrolled,
        LAStatus.PasscodeNotSet or LAStatus.BiometryNotAvailable or LAStatus.NotInteractive => BiometricResult.NotAvailable,
        _ => BiometricResult.Failed,
    };
}
#endif
