# Issue #602 — Spine.Authentication.Passkeys: Windows (webauthn.dll, Windows Hello)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/602
**Branch:** issue/602-spineauthenticationpasskeys-windows-webauthndll-wi
**Status:** Completed

## Plan

Step 5.4c in `docs/plans/ai-and-security.md`. #522 (PR #608) built everything platform-neutral; Windows only fills in
the ceremony.

### What #522 left for Windows

- **Platform-neutral, done:** `IPasskeyCeremony` (`Core/PasskeySignIn.cs`) takes the server's WebAuthn options JSON and
  returns the credential as `PublicKeyCredential.toJSON()`; `SpineAuthFlows.PasskeySignInAsync` /
  `PasskeyRegisterAsync` call `/auth/passkey/{signin,register}[/options]`; a `null` credential is `SignInResult.Canceled`
  and an `InvalidOperationException` is the sign-in's error. The sheet's buttons, "Add a passkey" (`LinkAsync("passkey")`),
  the Showcase's sign-in page and the server (`SpinePasskeys`, Fido2NetLib) need no change.
- **Per platform:** `SpinePasskeyExtensions.AddPasskeys` builds a `PasskeyProvider(IsSupported, SupportsAutoFill, …)`
  from `ApplePasskeys`; elsewhere `IsSupported` is false and the sheet leaves passkeys out.

### Relying party and origin on Windows

- The RP id is the one in the server's options (`rp.id` / `rpId`), passed to webauthn.dll as `pwszRpId`. Windows has no
  association file: any desktop app may ask for any RP id, and the Windows Hello dialog names the domain.
- webauthn.dll does not build `clientDataJSON`; the caller does and passes it in `WEBAUTHN_CLIENT_DATA` (SHA-256). Spine
  writes `{"type":"webauthn.create"|"webauthn.get","challenge":…,"origin":"https://<rpId>","crossOrigin":false}`. The
  server already accepts `https://<RelyingPartyId>` (Apple's origin), so **the server needs no change**.
- **Locally**, the Showcase server answers on `http://localhost:5100`, and passkeys are off there (no
  `Auth:Passkeys:RelyingPartyId` in Development). With `RelyingPartyId = localhost` the origin would be
  `https://localhost`, which WebAuthn allows. The scheme and port of the server's URL do not matter, because the origin
  comes from the RP id, not from the URL. A dev tunnel is not needed on Windows (it is only for iOS/Android, which fetch
  the association file from the RP's domain over https). The owner chose Azure for the verification, so this stays as
  it is.
- **Azure:** a Showcase built with `-p:SpineSampleServer=https://spine-showcase-e41ddd.azurewebsites.net/
  -p:SpinePasskeyDomain=spine-showcase-e41ddd.azurewebsites.net` works against the running server as it is.

### This machine

webauthn.dll 10.0.26100, `WebAuthNGetApiVersionNumber()` = 9, and
`WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable` = true. No TPM (`tpmtool`: "device is not ready"; no security
device): Windows Hello keeps the passkey in software, behind the PIN.

### Steps

1. `src/Plugin.Maui.Spine.Authentication.Passkeys/WindowsPasskeys.cs` (`#if WINDOWS`), an `IPasskeyCeremony`:
   - `[LibraryImport("webauthn.dll")]` for `WebAuthNGetApiVersionNumber`, `WebAuthNAuthenticatorMakeCredential`,
     `WebAuthNAuthenticatorGetAssertion`, `WebAuthNFreeCredentialAttestation`, `WebAuthNFreeAssertion`,
     `WebAuthNGetCancellationId`, `WebAuthNCancelCurrentOperation`, `WebAuthNGetErrorName`; the structs of API v1
     (make-credential options v3, get-assertion options v4), which have everything the server asks for: resident key
     required, user verification required, attestation none, the exclude list, the cancellation id.
   - `CreateAsync`: options JSON → RP / user / `pubKeyCredParams` / `excludeCredentials` / timeout; `clientDataJSON` as
     above; `WEBAUTHN_CREDENTIAL_ATTESTATION` → `{ id, rawId, type, authenticatorAttachment, response { clientDataJSON,
     attestationObject, transports }, clientExtensionResults }`. `authenticatorAttachment` from `dwUsedTransport`
     (internal → platform, otherwise cross-platform).
   - `GetAsync`: empty allow list (discoverable), UV required → `{ …, response { clientDataJSON, authenticatorData,
     signature, userHandle } }`. `autoFill` is never asked (no AutoFill on Windows).
   - The dialog is modal to the app's window: the HWND from the first MAUI window, as `BiometricAuth.Windows.cs` does.
     The blocking call runs off the UI thread; the cancellation token calls `WebAuthNCancelCurrentOperation`.
   - Results: `S_OK` → JSON; `NTE_USER_CANCELLED` (closed, or the PIN dialog cancelled) and a cancelled token → `null`
     (`Canceled`); `NTE_EXISTS` / InvalidStateError (an excluded passkey is on this device) → "This device already has a
     passkey for the account."; anything else → `InvalidOperationException` with `WebAuthNGetErrorName` and the HRESULT.
2. `SpinePasskeyExtensions.AddPasskeys` picks the ceremony per platform; `SupportsAutoFill` false on Windows.
3. The "not there yet" texts: `ApplePasskeys`'s `PlatformNotSupportedException`, `SpineAuth.PasskeyAsync`'s message,
   the package description and README, `AddPasskeys`' doc comment.
4. Showcase: no code change; the build switch (`SPINE_PASSKEYS`) turns passkeys on, with `SpineSampleServer` pointing
   at Azure.
5. Docs: `docs/wiki/authentication.md` (platforms, the Windows RP/origin note, what Windows Hello shows),
   `docs/wiki/authentication-server.md` if the local RP needs a line, the server README, `docs/wiki/packages.md`, the
   plan (5.4c status, decisions, "Last updated").
6. Verify: build `Spine.Packages.slnf` Release (all four TFMs), run the tests, start the server and the Showcase
   (unpackaged, from a Terminal tab), and the owner confirms: sign up with a passkey, sign out, sign in with it, "Add a
   passkey" to a signed-in account (and the excluded-credential refusal), cancel in the Windows dialog for each.
7. PR linking #602 and this changelog.

## Open Questions

None; answered by the owner on 2026-11-11 (Decisions).

## Changes

- `WindowsPasskeys.cs`: `IPasskeyCeremony` over `webauthn.dll` (`LibraryImport`, API v1 structs), the dialog over the
  first MAUI window's HWND, the call off the UI thread and cancelled through its cancellation id.
- `SpinePasskeyExtensions.AddPasskeys` uses it on Windows (no AutoFill); `AllowUnsafeBlocks` in the package's project.
- The "not there yet" texts (`ApplePasskeys`'s stub, `SpineAuth`'s message, the package description and README,
  `AddPasskeys`' doc comment) name Windows as supported.
- Docs: `docs/wiki/authentication.md` (Windows: the dialog, the origin, no association, no TPM, no AutoFill),
  `docs/wiki/packages.md`, `README.md`, the spine-setup skill, the server README, the plan (5.4c in review, decisions,
  history, "Last updated").
- Verified by the owner on 2026-11-11 in the Showcase on Windows (unpackaged, a Parallels VM without a TPM, PIN) against
  the Azure server: a new account with a passkey made in Windows Hello, sign-in with it, sign-in with a phone's passkey
  through the QR code, a closed dialog back to the sheet without an error, and "Add a passkey" refused on a PC that
  already has one for the account.
- Release build of `Spine.Packages.slnf` (android, ios, maccatalyst, windows) green. Tests in Release: every project
  passes except one in `Plugin.Maui.Spine.AI.Tests`, unrelated and Windows-only:
  `ContextTests.On_the_device_the_signed_in_user_comes_with_their_name_and_email_without_the_push_link` compares
  `TimeZoneInfo.Local` with the zone found by id, which on Windows has the same name but is not `Equals`.

## Decisions

- **2026-11-11, owner:** Spine's own P/Invoke against webauthn.dll, not `DSInternals.Win32.WebAuthn`: no third-party
  dependency for every Windows consumer of the package.
- **2026-11-11, owner:** Without webauthn.dll or API v1 (Windows 10 1809) `IsSupported` is false and the sheet leaves
  passkeys out. Without Windows Hello set up the buttons stay: Windows' dialog offers a phone or a security key.
- **2026-11-11, owner:** Verified against the Azure Showcase server (`spine-showcase-e41ddd.azurewebsites.net`), so the
  server's settings stay as they are; no local `RelyingPartyId`.
