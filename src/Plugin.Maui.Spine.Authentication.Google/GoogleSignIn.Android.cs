#if ANDROID
using Android.OS;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using Java.Util.Concurrent;
using Xamarin.GoogleAndroid.Libraries.Identity.GoogleId;
using JavaObject = Java.Lang.Object;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>Credential Manager's Google sheet, with the server's nonce in the ID token.</summary>
internal sealed class GoogleSignIn(SpineGoogleOptions options) : INativeSignIn
{
    // One thread for every callback: a new executor per call would leave a thread behind each time.
    private static readonly IExecutor Callbacks = Executors.NewSingleThreadExecutor()!;

    // Credential Manager needs Android 6.0; Spine's apps start lower, and there the broker in the browser takes over.
    public static bool IsSupported => OperatingSystem.IsAndroidVersionAtLeast(23);

    public async Task<NativeCredential?> SignInAsync(string nonce, CancellationToken cancellationToken)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Sign in with Google needs the app's activity, and there is none yet.");
        var manager = CredentialManager.Create(activity);

        // Google's order: the accounts that signed in to this app before, picked without a tap when there is one; then
        // every account on the device, the first time.
        try
        {
            return await RequestAsync(manager, activity, Option(nonce, signedInBefore: true), cancellationToken);
        }
        catch (NoCredentialException)
        {
        }

        try
        {
            return await RequestAsync(manager, activity, Option(nonce, signedInBefore: false), cancellationToken);
        }
        catch (NoCredentialException e)
        {
            throw new InvalidOperationException($"No Google account to sign in with: add one in the device's settings. ({e.Message})", e);
        }
    }

    // Clearing needs no activity: a refresh refused while the app is in the background signs out too.
    public Task SignedOutAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CredentialManager.Create(Platform.AppContext).ClearCredentialStateAsync(
            new ClearCredentialStateRequest(), null, Callbacks, new Callback(_ => done.TrySetResult(), e => done.TrySetException(Problem(e))));
        return done.Task;
    }

    private GetGoogleIdOption Option(string nonce, bool signedInBefore) => new GetGoogleIdOption.Builder()
        .SetServerClientId(options.ServerClientId!)
        .SetNonce(nonce)
        .SetFilterByAuthorizedAccounts(signedInBefore)
        .SetAutoSelectEnabled(signedInBefore)
        .Build();

    private static async Task<NativeCredential?> RequestAsync(ICredentialManager manager, Android.App.Activity activity, GetGoogleIdOption option, CancellationToken cancellationToken)
    {
        var request = new GetCredentialRequest.Builder().AddCredentialOption(option).Build();
        var signal = new CancellationSignal();
        var answer = new TaskCompletionSource<JavaObject?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // A cancelled request never calls back, so the wait ends here rather than there.
        await using var _ = cancellationToken.Register(() =>
        {
            signal.Cancel();
            answer.TrySetCanceled(cancellationToken);
        });

        manager.GetCredentialAsync(activity, request, signal, Callbacks,
            new Callback(result => answer.TrySetResult(result), e => answer.TrySetException(Problem(e))));

        try
        {
            var response = await answer.Task as GetCredentialResponse;
            var credential = response?.Credential ?? throw new InvalidOperationException("Credential Manager answered without a credential.");

            if (credential is not CustomCredential custom || custom.Type != GoogleIdTokenCredential.TypeGoogleIdTokenCredential)
                throw new InvalidOperationException($"Credential Manager answered with a {credential.Type}, not a Google ID token.");

            var google = GoogleIdTokenCredential.CreateFrom(custom.Data);
            return new NativeCredential(google.IdToken, google.DisplayName);
        }
        catch (GetCredentialCancellationException)
        {
            return null;
        }
    }

    // Credential Manager's own exceptions pass through as they are, so a cancel and "no account" can be told apart;
    // anything else is shown with its type, which says more than Google's message alone.
    private static Exception Problem(JavaObject error) =>
        JavaObject.GetObject<Java.Lang.Throwable>(error.Handle, Android.Runtime.JniHandleOwnership.DoNotTransfer) switch
        {
            GetCredentialCancellationException cancel => cancel,
            NoCredentialException none => none,

            // No Credential Manager provider: Android 13 or older without Google Play services. The broker takes over.
            GetCredentialProviderConfigurationException e => new PlatformNotSupportedException($"Credential Manager has no provider on this device ({e.ErrorMessage}).", e),
            GetCredentialException e => new InvalidOperationException($"Sign in with Google failed: {e.GetType().Name}: {e.ErrorMessage}", e),
            { } e => new InvalidOperationException($"Sign in with Google failed: {e.Message}", e),
            null => new InvalidOperationException($"Sign in with Google failed: {error}"),
        };

    private sealed class Callback(Action<JavaObject?> onResult, Action<JavaObject> onError) : JavaObject, ICredentialManagerCallback
    {
        public void OnResult(JavaObject? result) => onResult(result);

        public void OnError(JavaObject e) => onError(e);
    }
}
#endif
