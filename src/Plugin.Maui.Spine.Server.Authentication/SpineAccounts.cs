using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>What an email to the user is for.</summary>
public enum SpineAuthEmailKind
{
    /// <summary>A new account: the code that proves the email is theirs.</summary>
    VerifyEmail,

    /// <summary>A forgotten password: the code that lets them set a new one.</summary>
    ResetPassword,

    /// <summary>Someone signed up with an email that already has an account. No code; say how to sign in or reset.</summary>
    AlreadyRegistered,
}

/// <summary>An email the server wants sent.</summary>
/// <param name="Kind">What it is for.</param>
/// <param name="To">The address.</param>
/// <param name="Code">The code to type into the app, for <see cref="SpineAuthEmailKind.VerifyEmail"/> and <see cref="SpineAuthEmailKind.ResetPassword"/>.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
/// <param name="DisplayName">The user's name, when known.</param>
public sealed record SpineAuthEmail(SpineAuthEmailKind Kind, string To, string? Code, DateTimeOffset? ExpiresAt, string? DisplayName);

/// <summary>Sends the emails of email and password sign-in. The app registers one in its services; Spine writes no email itself.</summary>
public interface ISpineAuthEmailSender
{
    /// <summary>
    /// Sends <paramref name="email"/>. It is called without the request waiting, so the answer takes as long whether or
    /// not an email goes out; hand it to a queue or a mail service and return. An exception is logged.
    /// </summary>
    Task SendAsync(SpineAuthEmail email, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a sign-in step: tokens, or a refusal for the endpoint to answer.</summary>
internal sealed record SignInOutcome(SpineTokens? Tokens, int Status, SpineAuthProblem? Problem)
{
    public static SignInOutcome Ok(SpineTokens tokens) => new(tokens, 200, null);
    public static SignInOutcome Accepted { get; } = new(null, 202, null);
    public static SignInOutcome Refused(int status, string error, string? detail = null) => new(null, status, new SpineAuthProblem(error, detail));
}

internal sealed class SpineAccounts(
    SpineAuthOptions options, IUserResolver users, ISpineAuthStore store, SpineTokenService tokens, IdTokenValidator idTokens,
    AppleTokenClient? apple, ISpineAuthEmailSender? email, TimeProvider time, ILogger logger)
{
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
    private const int MaxPasswordLength = 1024;
    private const int MaxNameLength = 100;
    private const int MaxIdTokenLength = 16 * 1024;
    private const int Retries = 5;

    private readonly PasswordHasher<SpineAuthUser> _hasher = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Task, bool> _sending = new();

    // A hash to compare against when the email is unknown, so a wrong email takes as long as a wrong password.
    private readonly Lazy<string> _decoy = new(() => new PasswordHasher<SpineAuthUser>().HashPassword(null!, Guid.NewGuid().ToString()));

    public async Task<SignInOutcome> SignInWithIdTokenAsync(string providerName, SpineIdTokenRequest request, CancellationToken cancellationToken)
    {
        var (user, refusal) = await SignInUserAsync(providerName, request, null, false, cancellationToken);
        return user is null ? refusal! : SignInOutcome.Ok(await tokens.IssueAsync(user, providerName, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Validates an ID token and finds, links or makes its user. <paramref name="providerRefresh"/> is a provider refresh
    /// token the caller already has (the browser broker's exchange); without it, Apple's code in the request is traded.
    /// </summary>
    public async Task<(SpineAuthUser? User, SignInOutcome? Refusal)> SignInUserAsync(
        string providerName, SpineIdTokenRequest request, (string Token, string ClientId)? providerRefresh, bool brokered, CancellationToken cancellationToken)
    {
        if (!options.Providers.TryGetValue(providerName, out var provider))
            return (null, SignInOutcome.Refused(404, SpineAuthErrors.UnknownProvider));

        if (string.IsNullOrWhiteSpace(request.IdToken) || request.IdToken.Length > MaxIdTokenLength)
            return (null, SignInOutcome.Refused(400, SpineAuthErrors.InvalidRequest, "idToken is required and at most 16 KB."));

        var (token, problem) = await idTokens.ValidateAsync(providerName, provider, request, brokered, cancellationToken);
        if (token is null)
        {
            logger.LogInformation("Spine sign-in: a {Provider} ID token was refused: {Problem}.", providerName, problem);
            return (null, SignInOutcome.Refused(401, SpineAuthErrors.InvalidToken, problem));
        }

        var user = await users.FindByLoginAsync(providerName, token.Subject, cancellationToken);

        // An email the provider does not vouch for is not kept at all: the token's owner chose it, and an account that
        // owned someone else's address could later be handed to them through a password reset.
        // Microsoft vouches only for an email whose domain the tenant has verified (xms_edov).
        var verifiedEmail = provider.TrustEmail && token.EmailVerified || token.EmailDomainVerified ? token.Email : null;
        var name = Name(request.DisplayName) ?? Name(token.Name);
        var given = Name(request.GivenName) ?? Name(token.GivenName);
        var family = Name(request.FamilyName) ?? Name(token.FamilyName);

        // Only into an account whose own email is verified: an unverified sign-up could be someone else's claim on it.
        if (user is null && options.LinkByVerifiedEmail && verifiedEmail is not null
            && await users.FindByEmailAsync(verifiedEmail, cancellationToken) is { EmailVerified: true } owner)
        {
            user = owner;
        }

        if (user is null)
        {
            user = new SpineAuthUser
            {
                Id = Guid.NewGuid().ToString("N"),
                Email = verifiedEmail,
                EmailVerified = verifiedEmail is not null,
                DisplayName = name,
                PictureUrl = token.Picture,
                GivenName = given,
                FamilyName = family,
                Organization = token.Organization,
                CreatedAt = time.GetUtcNow(),
            };

            // Another account owns the email and linking is off: this one is made without it.
            if (!await users.CreateAsync(user, cancellationToken))
            {
                user.Email = null;
                user.EmailVerified = false;
                await users.CreateAsync(user, cancellationToken);
            }
        }
        else
        {
            // Names are kept as first given (the user may have changed them in the app); the picture and the organization
            // follow the provider the user signs in with.
            // Google and Microsoft say whether the account belongs to an organization, so a personal account clears it.
            var reportsOrganization = provider is SpineMicrosoftOptions || providerName == "google";

            bool Changes(SpineAuthUser u) =>
                u.DisplayName is null && name is not null || u.GivenName is null && given is not null || u.FamilyName is null && family is not null
                || token.Picture is not null && token.Picture != u.PictureUrl || reportsOrganization && token.Organization != u.Organization;

            if (Changes(user))
            {
                user = await ChangeAsync(user.Id, u =>
                {
                    if (!Changes(u)) return false;
                    u.DisplayName ??= name;
                    u.GivenName ??= given;
                    u.FamilyName ??= family;
                    u.PictureUrl = token.Picture ?? u.PictureUrl;
                    if (reportsOrganization) u.Organization = token.Organization;
                    return true;
                }, cancellationToken) ?? user;
            }
        }

        var login = new SpineAuthLogin(providerName, token.Subject, user.Id);

        if (providerRefresh is { } kept)
        {
            login = login with { ProviderRefreshToken = kept.Token, ProviderClientId = kept.ClientId };
        }
        else if (apple is not null && providerName == "apple" && request.AuthorizationCode is { Length: > 0 } code
            && await apple.ExchangeAsync(code, token.Audience, cancellationToken) is { } appleRefresh)
        {
            login = login with { ProviderRefreshToken = appleRefresh, ProviderClientId = token.Audience };
        }
        else if ((await users.GetLoginsAsync(user.Id, cancellationToken)).FirstOrDefault(l => l.Provider == providerName && l.Subject == token.Subject) is { } existing)
        {
            login = existing;
        }

        await users.SaveLoginAsync(login, cancellationToken);
        return (user, null);
    }

    public async Task<SignInOutcome> SignUpAsync(SpinePasswordSignUp request, CancellationToken cancellationToken)
    {
        var password = PasswordOptions();
        if (!password.AllowSignUp) return SignInOutcome.Refused(403, SpineAuthErrors.SignUpClosed);
        if (Email(request.Email) is not { } address) return SignInOutcome.Refused(400, SpineAuthErrors.InvalidRequest, "email is not an email address.");
        if (PasswordProblem(request.Password) is { } weak) return weak;

        var user = new SpineAuthUser
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = address,
            DisplayName = Name(request.DisplayName),
            CreatedAt = time.GetUtcNow(),
        };
        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        if (!await users.CreateAsync(user, cancellationToken))
        {
            if (!password.RequireVerifiedEmail) return SignInOutcome.Refused(409, SpineAuthErrors.EmailTaken);

            // The same answer as a new account, so sign-up does not tell who has one; the owner gets an email instead.
            Send(new SpineAuthEmail(SpineAuthEmailKind.AlreadyRegistered, address, null, null, null));
            return SignInOutcome.Accepted;
        }

        await users.SaveLoginAsync(new SpineAuthLogin(SpineAuthDefaults.PasswordProvider, user.Id, user.Id), cancellationToken);

        if (!password.RequireVerifiedEmail)
            return SignInOutcome.Ok(await tokens.IssueAsync(user, SpineAuthDefaults.PasswordProvider, cancellationToken: cancellationToken));

        await SendCodeAsync(SpineAuthEmailKind.VerifyEmail, user, cancellationToken);
        return SignInOutcome.Accepted;
    }

    public async Task<SignInOutcome> VerifyEmailAsync(SpineEmailCode request, CancellationToken cancellationToken)
    {
        if (await TakeCodeAsync(SpineAuthEmailKind.VerifyEmail, request.Email, request.Code, cancellationToken) is not { } userId
            || await ChangeAsync(userId, u => !u.EmailVerified && (u.EmailVerified = true), cancellationToken) is not { } user)
        {
            return SignInOutcome.Refused(400, SpineAuthErrors.InvalidCode);
        }

        return SignInOutcome.Ok(await tokens.IssueAsync(user, SpineAuthDefaults.PasswordProvider, cancellationToken: cancellationToken));
    }

    public async Task<SignInOutcome> ResendAsync(SpineEmailRequest request, CancellationToken cancellationToken)
    {
        PasswordOptions();
        if (Email(request.Email) is { } address && await users.FindByEmailAsync(address, cancellationToken) is { EmailVerified: false, PasswordHash: not null } user)
            await SendCodeAsync(SpineAuthEmailKind.VerifyEmail, user, cancellationToken);

        return SignInOutcome.Accepted;
    }

    public async Task<SignInOutcome> SignInWithPasswordAsync(SpinePasswordSignIn request, CancellationToken cancellationToken)
    {
        var password = PasswordOptions();
        var typed = request.Password ?? "";

        // Every write below is conditional on the user as read, so parallel guesses cannot each count from zero, and a
        // sign-in that read the password before a reset cannot finish after it: both read again and decide again.
        for (var attempt = 0; attempt < Retries; attempt++)
        {
            var now = time.GetUtcNow();
            var user = Email(request.Email) is { } address ? await users.FindByEmailAsync(address, cancellationToken) : null;

            // A locked account answers like a wrong password, so the lockout does not show which emails have accounts.
            if (user?.PasswordHash is not { } hash || typed.Length > MaxPasswordLength || user.LockedUntil > now)
            {
                _hasher.VerifyHashedPassword(null!, _decoy.Value, typed.Length > MaxPasswordLength ? "" : typed);
                return SignInOutcome.Refused(401, SpineAuthErrors.InvalidCredentials);
            }

            var result = _hasher.VerifyHashedPassword(user, hash, typed);
            if (result == PasswordVerificationResult.Failed)
            {
                user.FailedSignIns++;
                if (user.FailedSignIns >= password.MaxFailedSignIns)
                {
                    user.FailedSignIns = 0;
                    user.LockedUntil = now + password.LockoutDuration;
                    logger.LogWarning("Spine sign-in: user {User} is locked out until {Until:O} after {Count} wrong passwords.",
                        user.Id, user.LockedUntil, password.MaxFailedSignIns);
                }

                if (await users.UpdateAsync(user, cancellationToken)) return SignInOutcome.Refused(401, SpineAuthErrors.InvalidCredentials);
                continue;
            }

            user.FailedSignIns = 0;
            user.LockedUntil = null;
            if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = _hasher.HashPassword(user, typed);
            if (!await users.UpdateAsync(user, cancellationToken)) continue;

            if (password.RequireVerifiedEmail && !user.EmailVerified)
            {
                await SendCodeAsync(SpineAuthEmailKind.VerifyEmail, user, cancellationToken);
                return SignInOutcome.Refused(403, SpineAuthErrors.EmailNotVerified);
            }

            return SignInOutcome.Ok(await tokens.IssueAsync(user, SpineAuthDefaults.PasswordProvider, cancellationToken: cancellationToken));
        }

        logger.LogWarning("Spine sign-in: a password sign-in gave up after {Retries} conflicting writes.", Retries);
        return SignInOutcome.Refused(401, SpineAuthErrors.InvalidCredentials);
    }

    public async Task<SignInOutcome> ForgotAsync(SpineEmailRequest request, CancellationToken cancellationToken)
    {
        PasswordOptions();
        if (Email(request.Email) is { } address && await users.FindByEmailAsync(address, cancellationToken) is { } user)
            await SendCodeAsync(SpineAuthEmailKind.ResetPassword, user, cancellationToken);

        return SignInOutcome.Accepted;
    }

    public async Task<SignInOutcome> ResetAsync(SpinePasswordReset request, CancellationToken cancellationToken)
    {
        PasswordOptions();
        if (PasswordProblem(request.Password) is { } weak) return weak;

        if (await TakeCodeAsync(SpineAuthEmailKind.ResetPassword, request.Email, request.Code, cancellationToken) is not { } userId)
            return SignInOutcome.Refused(400, SpineAuthErrors.InvalidCode);

        // The code proves the email, and anyone holding an old session may be the reason for the reset.
        var user = await ChangeAsync(userId, u =>
        {
            u.PasswordHash = _hasher.HashPassword(u, request.Password);
            u.EmailVerified = true;
            u.FailedSignIns = 0;
            u.LockedUntil = null;
            return true;
        }, cancellationToken);

        if (user is null) return SignInOutcome.Refused(400, SpineAuthErrors.InvalidCode);

        await users.SaveLoginAsync(new SpineAuthLogin(SpineAuthDefaults.PasswordProvider, user.Id, user.Id), cancellationToken);
        await store.RevokeUserAsync(user.Id, cancellationToken);

        return SignInOutcome.Ok(await tokens.IssueAsync(user, SpineAuthDefaults.PasswordProvider, cancellationToken: cancellationToken));
    }

    public async Task DeleteAsync(string userId, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (options.OnAccountDeleting is { } deleting) await deleting(userId, services, cancellationToken);

        foreach (var login in await users.GetLoginsAsync(userId, cancellationToken))
        {
            if (login is { Provider: "apple", ProviderRefreshToken: { } appleToken, ProviderClientId: { } clientId } && apple is not null)
                await apple.RevokeAsync(appleToken, clientId, cancellationToken);
        }

        await store.RevokeUserAsync(userId, cancellationToken);
        await users.DeleteAsync(userId, cancellationToken);
        logger.LogInformation("Spine sign-in: user {User} deleted their account.", userId);
    }

    /// <summary>Reads the user, applies <paramref name="change"/> and saves it, reading again when someone else saved in between.</summary>
    /// <returns>The saved user, the user unchanged when <paramref name="change"/> returned <see langword="false"/>, or <see langword="null"/> when the user is gone.</returns>
    private async Task<SpineAuthUser?> ChangeAsync(string userId, Func<SpineAuthUser, bool> change, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Retries; attempt++)
        {
            if (await users.FindByIdAsync(userId, cancellationToken) is not { } user) return null;
            if (!change(user) || await users.UpdateAsync(user, cancellationToken)) return user;
        }

        throw new InvalidOperationException($"User {userId} kept changing; gave up after {Retries} tries.");
    }

    private SpineEmailPasswordOptions PasswordOptions() =>
        options.EmailPasswordOptions ?? throw new InvalidOperationException("Email and password sign-in is not configured.");

    private SignInOutcome? PasswordProblem(string? password)
    {
        var minimum = PasswordOptions().MinPasswordLength;
        return password is null || password.Length < minimum || password.Length > MaxPasswordLength
            ? SignInOutcome.Refused(400, SpineAuthErrors.WeakPassword, $"A password has {minimum} to {MaxPasswordLength} characters.")
            : null;
    }

    // One live code per purpose and email: a new one replaces the last, and any attempt uses it up, right or wrong.
    private async Task SendCodeAsync(SpineAuthEmailKind kind, SpineAuthUser user, CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetString(CodeAlphabet, 8);
        var expiresAt = time.GetUtcNow() + PasswordOptions().CodeLifetime;
        await store.SaveOneTimeAsync(CodeKey(kind, user.Email!), $"{user.Id}|{SpineTokenService.Hash(code)}", expiresAt, cancellationToken);
        Send(new SpineAuthEmail(kind, user.Email!, code, expiresAt, user.DisplayName));
    }

    private async Task<string?> TakeCodeAsync(SpineAuthEmailKind kind, string? email, string? code, CancellationToken cancellationToken)
    {
        if (Email(email) is not { } address || code is null) return null;

        var typed = new string(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (typed.Length != 8) return null;

        if (await store.TakeOneTimeAsync(CodeKey(kind, address), time.GetUtcNow(), cancellationToken) is not { } stored
            || stored.Split('|') is not [var userId, var expected])
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(SpineTokenService.Hash(typed)))
            ? userId
            : null;
    }

    // Sent without waiting, so an answer about an existing account takes no longer than one about a missing account.
    private void Send(SpineAuthEmail message)
    {
        var sending = Task.Run(async () =>
        {
            try
            {
                await email!.SendAsync(message, CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Spine sign-in: the {Kind} email could not be sent.", message.Kind);
            }
        });

        _sending[sending] = true;
        sending.ContinueWith(done => _sending.TryRemove(done, out _), TaskScheduler.Default);
    }

    /// <summary>Completes when every email handed to the sender so far has been sent. For tests.</summary>
    internal Task EmailsSentAsync() => Task.WhenAll(_sending.Keys);

    private static string CodeKey(SpineAuthEmailKind kind, string email) =>
        SpineTokenService.Hash($"{kind}|{email.Trim().ToUpperInvariant()}");

    private static string? Email(string? email) =>
        email?.Trim() is { Length: > 2 and <= 254 } trimmed && trimmed.IndexOf('@') is > 0 and var at && at < trimmed.Length - 1 && !trimmed.Contains(' ')
            ? trimmed
            : null;

    private static string? Name(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim() is var trimmed && trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
}
