#if IOS || MACCATALYST
using AuthenticationServices;
using Foundation;
using UIKit;
#endif

namespace Plugin.Maui.Spine.Authentication;

/// <summary>What Apple's sheet hands over: the identity token for the server, the one-time code and, the first time only, the name.</summary>
internal sealed record AppleCredential(string IdentityToken, string? AuthorizationCode, string? Name);

/// <summary>Sign in with Apple's own sheet, on iOS and Mac Catalyst.</summary>
internal static class AppleSignIn
{
#if IOS || MACCATALYST
    public static bool IsSupported => true;

    /// <param name="nonce">What Apple puts in the token: the SHA-256 of the server's nonce, as hex.</param>
    /// <param name="cancellationToken">Stops waiting for the sheet.</param>
    /// <returns>The credential, or <see langword="null"/> when the user canceled.</returns>
    public static async Task<AppleCredential?> RequestAsync(string nonce, CancellationToken cancellationToken)
    {
        var request = new ASAuthorizationAppleIdProvider().CreateRequest();
        request.RequestedScopes = [ASAuthorizationScope.FullName, ASAuthorizationScope.Email];
        request.Nonce = nonce;

        var done = new TaskCompletionSource<AppleCredential?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(done);

        ASAuthorizationController? controller = null;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            controller = new ASAuthorizationController([request]) { Delegate = handler, PresentationContextProvider = handler };
            controller.PerformRequests();
        });

        await using (cancellationToken.Register(() =>
        {
            done.TrySetCanceled(cancellationToken);
            if (OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16))
                MainThread.BeginInvokeOnMainThread(() => controller?.Cancel());
        }))
        {
            // The handler is the controller's delegate, which holds it only weakly; it has to live until Apple answers.
            var credential = await done.Task;
            GC.KeepAlive(handler);
            return credential;
        }
    }

    private sealed class Handler(TaskCompletionSource<AppleCredential?> done)
        : ASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
    {
        public override void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
        {
            if (authorization.GetCredential<ASAuthorizationAppleIdCredential>() is not { IdentityToken: { } token } credential)
            {
                done.TrySetException(new InvalidOperationException("Sign in with Apple returned no identity token."));
                return;
            }

            var name = credential.FullName is { } full
                ? string.Join(' ', new[] { full.GivenName, full.FamilyName }.Where(p => !string.IsNullOrWhiteSpace(p)))
                : null;

            done.TrySetResult(new AppleCredential(
                token.ToString(NSStringEncoding.UTF8),
                credential.AuthorizationCode?.ToString(NSStringEncoding.UTF8),
                string.IsNullOrEmpty(name) ? null : name));
        }

        public override void DidComplete(ASAuthorizationController controller, NSError error)
        {
            if (error.Code == (long)ASAuthorizationError.Canceled)
                done.TrySetResult(null);
            else
                done.TrySetException(new InvalidOperationException($"Sign in with Apple failed: {error.Domain} {error.Code}: {error.LocalizedDescription}"));
        }

        public UIWindow GetPresentationAnchor(ASAuthorizationController controller)
        {
            var windows = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>().SelectMany(s => s.Windows).ToList();
            return Platform.GetCurrentUIViewController()?.View?.Window
                ?? windows.FirstOrDefault(w => w.IsKeyWindow)
                ?? windows.First();
        }
    }
#else
    public static bool IsSupported => false;

    public static Task<AppleCredential?> RequestAsync(string nonce, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Sign in with Apple's native sheet is on iOS and Mac Catalyst; elsewhere it goes through the browser (#557).");
#endif
}
