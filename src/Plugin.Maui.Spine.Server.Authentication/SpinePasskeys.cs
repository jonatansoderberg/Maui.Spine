using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>
/// Passkey ceremonies over Fido2NetLib. A passkey is a login with provider <c>passkey</c> and the credential id
/// (base64url) as its subject; its user handle is the UTF-8 of the user id, so a discoverable sign-in names its user.
/// </summary>
internal sealed class SpinePasskeys(
    SpineAuthOptions options, IUserResolver users, ISpineAuthStore store, SpineTokenService tokens, TimeProvider time, ILogger logger)
{
    private const int MaxCredentialLength = 64 * 1024;
    private const int MaxNameLength = 100;

    // A credential id is at most 1023 bytes in WebAuthn; platform passkeys use 16 to 64. Longer ones would not fit a key
    // in Azure Tables once encoded.
    private const int MaxCredentialIdLength = 255;
    private const string Provider = SpineAuthDefaults.PasskeyProvider;

    private readonly SpinePasskeyOptions _options = options.PasskeyOptions!;
    private readonly Lazy<Fido2> _fido = new(() => new Fido2(Configuration(options.PasskeyOptions!)));

    internal static Fido2Configuration Configuration(SpinePasskeyOptions passkeys) => new()
    {
        RPID = passkeys.RelyingPartyId!,
        RPName = passkeys.RelyingPartyName,
        Origins = Origins(passkeys),
        Timeout = (uint)passkeys.ChallengeLifetime.TotalMilliseconds,
    };

    // iOS and macOS apps sign with the domain's own origin; an Android app with android:apk-key-hash:<its certificate>.
    private static HashSet<string> Origins(SpinePasskeyOptions passkeys)
    {
        var origins = new HashSet<string>(passkeys.Origins, StringComparer.Ordinal) { $"https://{passkeys.RelyingPartyId}" };
        foreach (var fingerprint in passkeys.AndroidApps.SelectMany(a => a.Sha256Fingerprints))
            origins.Add("android:apk-key-hash:" + Base64UrlEncoder.Encode(Convert.FromHexString(fingerprint.Replace(":", ""))));
        return origins;
    }

    /// <summary>
    /// <c>POST /auth/passkey/register/options</c>: a new passkey for <paramref name="caller"/>, or, without one, a new
    /// account that signs in with it.
    /// </summary>
    public async Task<(SpinePasskeyChallenge? Challenge, SignInOutcome? Refusal)> RegisterOptionsAsync(
        SpinePasskeyNewAccount? newAccount, SpineCaller? caller, CancellationToken cancellationToken)
    {
        Fido2User fidoUser;
        List<PublicKeyCredentialDescriptor> exclude = [];
        Ceremony ceremony;

        if (caller is not null)
        {
            if (await users.FindByIdAsync(caller.UserId, cancellationToken) is not { } user)
                return (null, SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant, "The account no longer exists."));

            var name = user.Email ?? user.DisplayName ?? user.Id;
            fidoUser = new Fido2User { Id = Encoding.UTF8.GetBytes(user.Id), Name = name, DisplayName = user.DisplayName ?? name };

            // The platform refuses a second passkey for the same account on one authenticator, rather than replacing the first.
            exclude = (await users.GetLoginsAsync(user.Id, cancellationToken))
                .Where(l => l.Provider == Provider)
                .Select(l => new PublicKeyCredentialDescriptor(Base64UrlEncoder.DecodeBytes(l.Subject)))
                .ToList();
            ceremony = new Ceremony(Kind.Register, "", Caller: caller);
        }
        else
        {
            if (!_options.AllowSignUp) return (null, SignInOutcome.Refused(403, SpineAuthErrors.SignUpClosed));

            var displayName = Trim(newAccount?.DisplayName);
            var email = Trim(newAccount?.Email);
            if (displayName is null && email is null)
                return (null, SignInOutcome.Refused(400, SpineAuthErrors.InvalidRequest, "A new passkey account needs a name or an email: the passkey sheet shows it."));

            var userId = Guid.NewGuid().ToString("N");
            fidoUser = new Fido2User { Id = Encoding.UTF8.GetBytes(userId), Name = email ?? displayName!, DisplayName = displayName ?? email! };
            ceremony = new Ceremony(Kind.Register, "", NewUserId: userId, DisplayName: displayName, Email: email);
        }

        var creation = _fido.Value.RequestNewCredential(new RequestNewCredentialParams
        {
            User = fidoUser,
            ExcludeCredentials = exclude,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
            Extensions = new AuthenticationExtensionsClientInputs { CredProps = true },
        });

        var json = creation.ToJson();
        return (await SaveAsync(ceremony with { Options = json }, cancellationToken), null);
    }

    /// <summary><c>POST /auth/passkey/register</c>: the platform's new passkey, checked and kept; the user signs in with it.</summary>
    public async Task<SignInOutcome> RegisterAsync(SpinePasskeyResponse response, CancellationToken cancellationToken)
    {
        if (await TakeAsync(response.ChallengeId, Kind.Register, cancellationToken) is not { } ceremony)
            return SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant, "The passkey request expired or was already used; start again.");

        if (Read<AuthenticatorAttestationRawResponse>(response.Credential) is not { RawId.Length: > 0 and <= MaxCredentialIdLength } attestation)
            return SignInOutcome.Refused(400, SpineAuthErrors.InvalidRequest, "credential is not a WebAuthn registration, or its id is longer than 255 bytes.");

        RegisteredPublicKeyCredential credential;
        try
        {
            credential = await _fido.Value.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestation,
                OriginalOptions = CredentialCreateOptions.FromJson(ceremony.Options),
                IsCredentialIdUniqueToUserCallback = async (p, ct) =>
                    await users.FindByLoginAsync(Provider, Base64UrlEncoder.Encode(p.CredentialId), ct) is null,
            }, cancellationToken);
        }
        catch (Fido2VerificationException e)
        {
            logger.LogInformation("Spine sign-in: a passkey registration was refused: {Problem}.", e.Message);
            return SignInOutcome.Refused(401, SpineAuthErrors.InvalidToken, e.Message);
        }

        SpineAuthUser user;
        if (ceremony.Caller is { } caller)
        {
            if (await users.FindByIdAsync(caller.UserId, cancellationToken) is not { } existing)
                return SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant, "The account no longer exists.");
            user = existing;
        }
        else
        {
            // The email is the user's own claim, so it stays on the login, unverified, and not on the account.
            user = new SpineAuthUser { Id = ceremony.NewUserId!, DisplayName = ceremony.DisplayName, CreatedAt = time.GetUtcNow() };
            await users.CreateAsync(user, cancellationToken);
        }

        var login = new SpineAuthLogin(Provider, Base64UrlEncoder.Encode(credential.Id), user.Id)
        {
            LinkedAt = time.GetUtcNow(),
            Email = ceremony.Email,
            DisplayName = ceremony.DisplayName ?? user.DisplayName,
            PasskeyPublicKey = credential.PublicKey,
            PasskeySignCount = credential.SignCount,
        };
        try
        {
            await users.SaveLoginAsync(login, cancellationToken);
        }
        catch when (ceremony.Caller is null)
        {
            // A new account without its passkey could never be signed in to.
            await users.DeleteAsync(user.Id, CancellationToken.None);
            throw;
        }

        logger.LogInformation("Spine sign-in: user {User} added a passkey.", user.Id);
        return SignInOutcome.Ok(await tokens.IssueAsync(user, ceremony.Caller?.Provider ?? Provider, cancellationToken: cancellationToken));
    }

    /// <summary><c>POST /auth/passkey/signin/options</c>: a sign-in with any of the domain's passkeys the device has.</summary>
    public async Task<SpinePasskeyChallenge> SignInOptionsAsync(CancellationToken cancellationToken)
    {
        var assertion = _fido.Value.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        });
        return await SaveAsync(new Ceremony(Kind.SignIn, assertion.ToJson()), cancellationToken);
    }

    /// <summary><c>POST /auth/passkey/signin</c>: the platform's signed answer, checked against the kept passkey.</summary>
    public async Task<SignInOutcome> SignInAsync(SpinePasskeyResponse response, CancellationToken cancellationToken)
    {
        if (await TakeAsync(response.ChallengeId, Kind.SignIn, cancellationToken) is not { } ceremony)
            return SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant, "The passkey request expired or was already used; start again.");

        if (Read<AuthenticatorAssertionRawResponse>(response.Credential) is not { RawId.Length: > 0 } assertion)
            return SignInOutcome.Refused(400, SpineAuthErrors.InvalidRequest, "credential is not a WebAuthn assertion.");

        var subject = Base64UrlEncoder.Encode(assertion.RawId);
        var user = await users.FindByLoginAsync(Provider, subject, cancellationToken);
        var login = user is null ? null
            : (await users.GetLoginsAsync(user.Id, cancellationToken)).FirstOrDefault(l => l.Provider == Provider && l.Subject == subject);
        if (user is null || login?.PasskeyPublicKey is null)
            return SignInOutcome.Refused(401, SpineAuthErrors.UnknownPasskey, "This passkey is not known here: it may have been removed from the account.");

        VerifyAssertionResult result;
        try
        {
            result = await _fido.Value.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertion,
                OriginalOptions = AssertionOptions.FromJson(ceremony.Options),
                StoredPublicKey = login.PasskeyPublicKey,
                StoredSignatureCounter = login.PasskeySignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (p, _) =>
                    Task.FromResult(CryptographicOperations.FixedTimeEquals(p.UserHandle, Encoding.UTF8.GetBytes(user.Id))),
            }, cancellationToken);
        }
        catch (Fido2VerificationException e)
        {
            logger.LogInformation("Spine sign-in: a passkey sign-in for user {User} was refused: {Problem}.", user.Id, e.Message);
            return SignInOutcome.Refused(401, SpineAuthErrors.InvalidToken, e.Message);
        }

        // Saved only while the passkey is still there: a removal during the sign-in must not bring it back.
        if (result.SignCount != login.PasskeySignCount && await users.FindByLoginAsync(Provider, subject, cancellationToken) is not null)
            await users.SaveLoginAsync(login with { PasskeySignCount = result.SignCount }, cancellationToken);

        return SignInOutcome.Ok(await tokens.IssueAsync(user, Provider, cancellationToken: cancellationToken));
    }

    private async Task<SpinePasskeyChallenge> SaveAsync(Ceremony ceremony, CancellationToken cancellationToken)
    {
        var id = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        await store.SaveOneTimeAsync(Key(id), JsonSerializer.Serialize(ceremony), time.GetUtcNow() + _options.ChallengeLifetime, cancellationToken);
        return new SpinePasskeyChallenge(id, ceremony.Options);
    }

    private async Task<Ceremony?> TakeAsync(string? id, Kind kind, CancellationToken cancellationToken) =>
        id is { Length: > 0 } && await store.TakeOneTimeAsync(Key(id), time.GetUtcNow(), cancellationToken) is { } saved
            && JsonSerializer.Deserialize<Ceremony>(saved) is { } ceremony && ceremony.Kind == kind
            ? ceremony
            : null;

    private static string Key(string id) => SpineTokenService.Hash($"passkey|{id}");

    private static T? Read<T>(string? json) where T : class
    {
        if (json is not { Length: > 0 and <= MaxCredentialLength }) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Trim(string? value) =>
        value?.Trim() is { Length: > 0 } trimmed ? trimmed[..Math.Min(trimmed.Length, MaxNameLength)] : null;

    internal enum Kind { Register, SignIn }

    /// <summary>A ceremony between its options and the platform's answer, kept once in the one-time store.</summary>
    internal sealed record Ceremony(
        Kind Kind, string Options, SpineCaller? Caller = null, string? NewUserId = null, string? DisplayName = null, string? Email = null);
}
