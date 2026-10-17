#if IOS || MACCATALYST
using System.Buffers.Text;
using System.Text.Json.Nodes;
using AuthenticationServices;
using Foundation;
using UIKit;
#endif

namespace Plugin.Maui.Spine.Authentication;

/// <summary>Passkeys through <c>ASAuthorizationPlatformPublicKeyCredentialProvider</c>, on iOS and Mac Catalyst 16 or later.</summary>
internal sealed class ApplePasskeys : IPasskeyCeremony
{
#if IOS || MACCATALYST
    public static bool IsSupported => OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16);

    // The keyboard's suggestions are an iOS thing; a Mac shows its own passkey sheet from the button.
    public static bool SupportsAutoFill => OperatingSystem.IsIOSVersionAtLeast(16) && !OperatingSystem.IsMacCatalyst();

    private ASAuthorizationController? _autoFill;
    private Task _modal = Task.CompletedTask;

    public async Task<string?> CreateAsync(string options, CancellationToken cancellationToken)
    {
        var json = JsonNode.Parse(options)!;
        var user = json["user"]!;
        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider((string)json["rp"]!["id"]!);
        var request = provider.CreateCredentialRegistrationRequest(Data(json["challenge"]), (string)user["name"]!, Data(user["id"]));
        request.UserVerificationPreference = ASAuthorizationPublicKeyCredentialUserVerificationPreference.Required;

        // Without it, a second passkey for the account on this device would replace the first silently. iOS 17.4 has
        // excludedCredentials on the request; the binding has it only on the browser protocol the request conforms to.
        if (json["excludeCredentials"] is JsonArray { Count: > 0 } exclude && request.RespondsToSelector(new ObjCRuntime.Selector("setExcludedCredentials:"))
            && ObjCRuntime.Runtime.GetINativeObject<IASAuthorizationWebBrowserPlatformPublicKeyCredentialRegistrationRequest>(request.Handle, false) is { } excluding)
        {
            excluding.ExcludedCredentials = exclude.Select(c => new ASAuthorizationPlatformPublicKeyCredentialDescriptor(Data(c!["id"]))).ToArray();
        }

        if (await ModalAsync(request, cancellationToken) is not { } authorization) return null;

        if (authorization.GetCredential<ASAuthorizationPlatformPublicKeyCredentialRegistration>() is not { RawAttestationObject: { } attestation } credential)
            throw new InvalidOperationException("The passkey sheet returned no attestation.");

        return new JsonObject
        {
            ["id"] = Text(credential.CredentialId),
            ["rawId"] = Text(credential.CredentialId),
            ["type"] = "public-key",
            ["authenticatorAttachment"] = "platform",
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Text(credential.RawClientDataJson),
                ["attestationObject"] = Text(attestation),
                ["transports"] = new JsonArray("internal", "hybrid"),
            },
            ["clientExtensionResults"] = new JsonObject(),
        }.ToJsonString();
    }

    public async Task<string?> GetAsync(string options, bool autoFill, CancellationToken cancellationToken)
    {
        var json = JsonNode.Parse(options)!;
        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider((string)json["rpId"]!);
        var request = provider.CreateCredentialAssertionRequest(Data(json["challenge"]));
        request.UserVerificationPreference = ASAuthorizationPublicKeyCredentialUserVerificationPreference.Required;

        var authorization = autoFill ? await AutoFillAsync(request, cancellationToken) : await ModalAsync(request, cancellationToken);
        if (authorization is null) return null;

        if (authorization.GetCredential<ASAuthorizationPlatformPublicKeyCredentialAssertion>() is not { Signature: { } signature } credential)
            throw new InvalidOperationException("The passkey sheet returned no signature.");

        return new JsonObject
        {
            ["id"] = Text(credential.CredentialId),
            ["rawId"] = Text(credential.CredentialId),
            ["type"] = "public-key",
            ["authenticatorAttachment"] = "platform",
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Text(credential.RawClientDataJson),
                ["authenticatorData"] = Text(credential.RawAuthenticatorData),
                ["signature"] = Text(signature),
                ["userHandle"] = Text(credential.UserId),
            },
            ["clientExtensionResults"] = new JsonObject(),
        }.ToJsonString();
    }

    // A sheet from a button. A pending AutoFill request has to go first: iOS runs one request at a time.
    private async Task<ASAuthorization?> ModalAsync(ASAuthorizationRequest request, CancellationToken cancellationToken)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _autoFill?.Cancel();
            _autoFill = null;
        });

        var modal = PerformAsync(request, autoFill: false, cancellationToken);
        _modal = modal;
        return await modal;
    }

    // The keyboard's suggestions, started only once no sheet is up, so the sheet the user opened is not cancelled.
    private async Task<ASAuthorization?> AutoFillAsync(ASAuthorizationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _modal.WaitAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return await PerformAsync(request, autoFill: true, cancellationToken);
    }

    private async Task<ASAuthorization?> PerformAsync(ASAuthorizationRequest request, bool autoFill, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var done = new TaskCompletionSource<ASAuthorization?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(done);

        ASAuthorizationController? controller = null;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            controller = new ASAuthorizationController([request]) { Delegate = handler, PresentationContextProvider = handler };
#if IOS
            if (autoFill)
            {
                _autoFill = controller;
                controller.PerformAutoFillAssistedRequests();
                return;
            }
#endif
            controller.PerformRequests();
        });

        await using (cancellationToken.Register(() =>
        {
            done.TrySetCanceled(cancellationToken);
            MainThread.BeginInvokeOnMainThread(() => controller?.Cancel());
        }))
        {
            try
            {
                // The handler is the controller's delegate, which holds it only weakly; it has to live until iOS answers.
                var authorization = await done.Task;
                GC.KeepAlive(handler);
                return authorization;
            }
            finally
            {
                if (autoFill) MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_autoFill == controller) _autoFill = null;
                });
            }
        }
    }

    private static NSData Data(JsonNode? base64Url) => NSData.FromArray(Base64Url.DecodeFromChars(((string?)base64Url ?? "").AsSpan()));

    private static string? Text(NSData? data) => data is null ? null : Base64Url.EncodeToString(data.ToArray());

    private sealed class Handler(TaskCompletionSource<ASAuthorization?> done)
        : ASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
    {
        public override void DidComplete(ASAuthorizationController controller, ASAuthorization authorization) => done.TrySetResult(authorization);

        public override void DidComplete(ASAuthorizationController controller, NSError error)
        {
            if (error.Code == (long)ASAuthorizationError.Canceled)
                done.TrySetResult(null);
            else
                done.TrySetException(new InvalidOperationException($"The passkey sheet failed: {error.Domain} {error.Code}: {error.LocalizedDescription}"));
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

    public static bool SupportsAutoFill => false;

    public Task<string?> CreateAsync(string options, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Spine's passkeys are on iOS and Mac Catalyst so far; Android is #601 and Windows #602.");

    public Task<string?> GetAsync(string options, bool autoFill, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Spine's passkeys are on iOS and Mac Catalyst so far; Android is #601 and Windows #602.");
#endif
}
