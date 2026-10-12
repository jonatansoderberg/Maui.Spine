#if ANDROID
using Android.App;
using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using Authenticators = AndroidX.Biometric.BiometricManager.Authenticators;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary><see cref="IBiometricAuth"/> on AndroidX <c>BiometricPrompt</c>: fingerprint or face, with the screen lock to fall back on.</summary>
internal sealed class BiometricAuth : IBiometricAuth
{
    public Task<BiometricAvailability> GetAvailabilityAsync()
    {
        var context = Platform.AppContext;
        var manager = BiometricManager.From(context);
        var biometric = manager.CanAuthenticate(Authenticators.BiometricWeak);
        var kind = biometric is BiometricManager.BiometricErrorNoHardware or BiometricManager.BiometricErrorHwUnavailable
            ? BiometricKind.None
            : BiometricKind.Biometric;
        var keyguard = (KeyguardManager?)context.GetSystemService(Android.Content.Context.KeyguardService);

        return Task.FromResult(new BiometricAvailability(kind, biometric == BiometricManager.BiometricSuccess, keyguard?.IsDeviceSecure == true));
    }

    public Task<BiometricResult> AuthenticateAsync(string reason, BiometricOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new BiometricOptions();

        if (Platform.CurrentActivity is not FragmentActivity activity)
            return Task.FromResult(BiometricResult.NotAvailable);

        var result = new TaskCompletionSource<BiometricResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new BiometricPrompt(activity, ContextCompat.GetMainExecutor(activity)!, new Callback(result));

        var info = new BiometricPrompt.PromptInfo.Builder()
            .SetTitle(options.Title ?? reason);

        if (options.Title is not null)
            info.SetSubtitle(reason);

        if (options.AllowDeviceCredential)
            info.SetAllowedAuthenticators(Authenticators.BiometricWeak | Authenticators.DeviceCredential);
        else
            info.SetAllowedAuthenticators(Authenticators.BiometricWeak)
                .SetNegativeButtonText(BiometricsStrings.Get("Spine.Biometrics.Cancel"));

        var registration = cancellationToken.Register(prompt.CancelAuthentication);
        result.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);

        prompt.Authenticate(info.Build());
        return result.Task;
    }

    private sealed class Callback(TaskCompletionSource<BiometricResult> result) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult authResult) =>
            result.TrySetResult(BiometricResult.Success);

        // A face or finger that was not recognised; the prompt stays up for another try.
        public override void OnAuthenticationFailed() { }

        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString) =>
            result.TrySetResult(errorCode switch
            {
                BiometricPrompt.ErrorUserCanceled or BiometricPrompt.ErrorNegativeButton or BiometricPrompt.ErrorCanceled => BiometricResult.Canceled,
                BiometricPrompt.ErrorLockout or BiometricPrompt.ErrorLockoutPermanent => BiometricResult.LockedOut,
                BiometricPrompt.ErrorNoBiometrics => BiometricResult.NotEnrolled,
                BiometricPrompt.ErrorHwNotPresent or BiometricPrompt.ErrorHwUnavailable or BiometricPrompt.ErrorNoDeviceCredential => BiometricResult.NotAvailable,
                _ => BiometricResult.Failed,
            });
    }
}
#endif
