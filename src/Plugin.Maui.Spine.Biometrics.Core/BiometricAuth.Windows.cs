#if WINDOWS
using Windows.Security.Credentials.UI;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary><see cref="IBiometricAuth"/> on Windows Hello: face, fingerprint or PIN.</summary>
internal sealed class BiometricAuth : IBiometricAuth
{
    public async Task<BiometricAvailability> GetAvailabilityAsync()
    {
        var availability = await UserConsentVerifier.CheckAvailabilityAsync();
        var available = availability == UserConsentVerifierAvailability.Available;

        return new BiometricAvailability(
            availability == UserConsentVerifierAvailability.DeviceNotPresent ? BiometricKind.None : BiometricKind.WindowsHello,
            available,
            available);
    }

    public async Task<BiometricResult> AuthenticateAsync(string reason, BiometricOptions? options = null, CancellationToken cancellationToken = default)
    {
        // A desktop app asks for its own window; RequestVerificationAsync is for UWP's CoreWindow.
        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            return BiometricResult.NotAvailable;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd, reason).AsTask(cancellationToken);

        return result switch
        {
            UserConsentVerificationResult.Verified => BiometricResult.Success,
            UserConsentVerificationResult.Canceled => BiometricResult.Canceled,
            UserConsentVerificationResult.RetriesExhausted => BiometricResult.LockedOut,
            UserConsentVerificationResult.NotConfiguredForUser => BiometricResult.NotEnrolled,
            UserConsentVerificationResult.DeviceNotPresent or UserConsentVerificationResult.DisabledByPolicy or UserConsentVerificationResult.DeviceBusy => BiometricResult.NotAvailable,
            _ => BiometricResult.Failed,
        };
    }
}
#endif
