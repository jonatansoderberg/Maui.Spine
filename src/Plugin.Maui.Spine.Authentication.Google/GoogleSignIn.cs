#if !ANDROID
namespace Plugin.Maui.Spine.Authentication;

// iOS, Mac Catalyst and Windows have no Google sheet: the GoogleSignIn iOS binding is no longer published, and the
// server's broker covers them through the browser.
internal sealed class GoogleSignIn(SpineGoogleOptions options) : INativeSignIn
{
    public static bool IsSupported => false;

    public Task<NativeCredential?> SignInAsync(string nonce, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException($"Google's own sheet is Android only; {options.DisplayName} goes through the browser here.");

    public Task SignedOutAsync() => Task.CompletedTask;
}
#endif
