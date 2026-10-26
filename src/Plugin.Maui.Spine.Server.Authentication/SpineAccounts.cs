using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

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

/// <summary>The signed-in user behind a request: who, and how they signed in this time.</summary>
internal sealed record SpineCaller(string UserId, string Provider);

/// <summary>The outcome of a sign-in step: tokens, or a refusal for the endpoint to answer.</summary>
internal sealed record SignInOutcome(SpineTokens? Tokens, int Status, SpineAuthProblem? Problem)
{
    public static SignInOutcome Ok(SpineTokens tokens) => new(tokens, 200, null);
    public static SignInOutcome Accepted { get; } = new(null, 202, null);
    public static SignInOutcome Pending(SpinePendingIdentity pending) => new(null, 202, null) { PendingIdentity = pending };
    public SpinePendingIdentity? PendingIdentity { get; init; }
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
        string providerName, SpineIdTokenRequest request, (string Token, string ClientId)? providerRefresh, bool brokered, CancellationToken cancellationToken,
        string? linkTo = null)
    {
        var (identity, refusal) = await IdentityAsync(providerName, request, providerRefresh, brokered, cancellationToken);
        return identity is null ? (null, refusal) : await ResolveAsync(identity, linkTo, cancellationToken);
    }

    /// <summary>
    /// The browser broker's link: the identity was validated at the provider's callback and is linked only here, at the
    /// code exchange, which PKCE ties to the device that started it. Linking at the callback would let a link ticket in a
    /// URL someone else opens attach their provider account to the ticket's owner.
    /// </summary>
    public async Task<SignInOutcome> LinkBrokeredAsync(Identity identity, SpineCaller caller, CancellationToken cancellationToken)
    {
        var (user, refusal) = await ResolveAsync(identity, caller.UserId, cancellationToken);
        return user is null ? refusal! : SignInOutcome.Ok(await tokens.IssueAsync(user, caller.Provider, cancellationToken: cancellationToken));
    }

    /// <summary>Validates an ID token and describes its identity, without finding or making a user.</summary>
    public async Task<(Identity? Identity, SignInOutcome? Refusal)> IdentityAsync(
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

        // An email the provider does not vouch for is not kept on the account: the token's owner chose it, and an account
        // that owned someone else's address could later be handed to them through a password reset.
        // Microsoft vouches only for an email whose domain the tenant has verified (xms_edov).
        var identity = new Identity(
            providerName, token.Subject,
            VerifiedEmail: provider.TrustEmail && token.EmailVerified || token.EmailDomainVerified ? token.Email : null,
            Email: token.Email,
            Name: Name(request.DisplayName) ?? Name(token.Name),
            GivenName: Name(request.GivenName) ?? Name(token.GivenName),
            FamilyName: Name(request.FamilyName) ?? Name(token.FamilyName),
            Picture: token.Picture,
            Organization: token.Organization,
            // Google and Microsoft say whether the account belongs to an organization, so a personal account clears it.
            ReportsOrganization: provider is SpineMicrosoftOptions || providerName == "google",
            ProviderRefreshToken: providerRefresh?.Token,
            ProviderClientId: providerRefresh?.ClientId);

        // Apple's code works once, so it is traded now, even when the account is only pending.
        if (identity.ProviderRefreshToken is null && apple is not null && providerName == "apple" && request.AuthorizationCode is { Length: > 0 } code
            && await apple.ExchangeAsync(code, token.Audience, cancellationToken) is { } appleRefresh)
        {
            identity = identity with { ProviderRefreshToken = appleRefresh, ProviderClientId = token.Audience };
        }

        return (identity, null);
    }

    /// <summary>
    /// What a validated sign-in becomes. With <paramref name="linkTo"/>, the identity is added to that user, unless
    /// another user has it. Without, the identity's user signs in; an unknown identity links by verified email, waits
    /// as a pending identity (<see cref="SpineAuthOptions.AskBeforeNewAccount"/>), or makes a new user.
    /// </summary>
    private async Task<(SpineAuthUser? User, SignInOutcome? Refusal)> ResolveAsync(Identity identity, string? linkTo, CancellationToken cancellationToken)
    {
        var user = await users.FindByLoginAsync(identity.Provider, identity.Subject, cancellationToken);
        var created = false;

        if (linkTo is not null)
        {
            if (user is not null && user.Id != linkTo)
            {
                return (null, SignInOutcome.Refused(409, SpineAuthErrors.IdentityInUse,
                    $"This {identity.Provider} account already signs in to another account. Sign in with it there and remove it, then link it here."));
            }

            user ??= await users.FindByIdAsync(linkTo, cancellationToken);
            if (user is null) return (null, SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant, "The account to link to no longer exists."));
        }
        else if (user is null)
        {
            // Only into an account whose own email is verified: an unverified sign-up could be someone else's claim on it.
            if (options.LinkByVerifiedEmail && identity.VerifiedEmail is { } verified
                && await users.FindByEmailAsync(verified, cancellationToken) is { EmailVerified: true } owner)
            {
                user = owner;
            }
            else if (options.AskBeforeNewAccount)
            {
                return (null, SignInOutcome.Pending(await PendAsync(identity, cancellationToken)));
            }
            else
            {
                user = await CreateAsync(identity, cancellationToken);
                created = true;
            }
        }

        // A link leaves the account's profile as it is: a personal account linked to a work one must not clear its organization.
        if (!created && linkTo is null) user = await UpdateProfileAsync(user, identity, cancellationToken);

        var existing = (await users.GetLoginsAsync(user.Id, cancellationToken))
            .FirstOrDefault(l => l.Provider == identity.Provider && l.Subject == identity.Subject);
        var login = (existing ?? new SpineAuthLogin(identity.Provider, identity.Subject, user.Id) { LinkedAt = time.GetUtcNow() }) with
        {
            Email = identity.Email ?? existing?.Email,
            DisplayName = identity.Name ?? existing?.DisplayName,
            // A link is a use too: the user just signed in with the provider to add it.
            LastUsedAt = time.GetUtcNow(),
        };
        if (identity.ProviderRefreshToken is { } refresh)
            login = login with { ProviderRefreshToken = refresh, ProviderClientId = identity.ProviderClientId };

        await users.SaveLoginAsync(login, cancellationToken);
        return (user, null);
    }

    private async Task<SpineAuthUser> CreateAsync(Identity identity, CancellationToken cancellationToken)
    {
        var user = new SpineAuthUser
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = identity.VerifiedEmail,
            EmailVerified = identity.VerifiedEmail is not null,
            DisplayName = identity.Name,
            PictureUrl = identity.Picture,
            GivenName = identity.GivenName,
            FamilyName = identity.FamilyName,
            Organization = identity.Organization,
            CreatedAt = time.GetUtcNow(),
        };

        // Another account owns the email and linking is off: this one is made without it.
        if (!await users.CreateAsync(user, cancellationToken))
        {
            user.Email = null;
            user.EmailVerified = false;
            await users.CreateAsync(user, cancellationToken);
        }

        return user;
    }

    // Names are kept as first given (the user may have changed them in the app); the picture and the organization follow
    // the provider the user signs in with.
    private async Task<SpineAuthUser> UpdateProfileAsync(SpineAuthUser user, Identity identity, CancellationToken cancellationToken)
    {
        bool Changes(SpineAuthUser u) =>
            u.DisplayName is null && identity.Name is not null || u.GivenName is null && identity.GivenName is not null
            || u.FamilyName is null && identity.FamilyName is not null
            || identity.Picture is not null && identity.Picture != u.PictureUrl
            || identity.ReportsOrganization && identity.Organization != u.Organization;

        if (!Changes(user)) return user;

        return await ChangeAsync(user.Id, u =>
        {
            if (!Changes(u)) return false;
            u.DisplayName ??= identity.Name;
            u.GivenName ??= identity.GivenName;
            u.FamilyName ??= identity.FamilyName;
            u.PictureUrl = identity.Picture ?? u.PictureUrl;
            if (identity.ReportsOrganization) u.Organization = identity.Organization;
            return true;
        }, cancellationToken) ?? user;
    }

    private async Task<SpinePendingIdentity> PendAsync(Identity identity, CancellationToken cancellationToken)
    {
        var ticket = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = time.GetUtcNow() + options.PendingIdentityLifetime;
        await store.SaveOneTimeAsync(PendingKey(ticket), JsonSerializer.Serialize(identity), expiresAt, cancellationToken);
        return new SpinePendingIdentity(ticket, identity.Provider, identity.VerifiedEmail, identity.Name, expiresAt);
    }

    /// <summary><c>POST /auth/pending/create</c>: the user chose a new account for the pending identity.</summary>
    public async Task<SignInOutcome> CreatePendingAsync(SpinePendingRequest request, CancellationToken cancellationToken)
    {
        if (await TakePendingAsync(request.Ticket, cancellationToken) is not { } identity) return SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant);

        var user = await users.FindByLoginAsync(identity.Provider, identity.Subject, cancellationToken);
        if (user is null)
        {
            user = await CreateAsync(identity, cancellationToken);
            await users.SaveLoginAsync(new SpineAuthLogin(identity.Provider, identity.Subject, user.Id)
            {
                LinkedAt = time.GetUtcNow(),
                LastUsedAt = time.GetUtcNow(),
                Email = identity.Email,
                DisplayName = identity.Name,
                ProviderRefreshToken = identity.ProviderRefreshToken,
                ProviderClientId = identity.ProviderClientId,
            }, cancellationToken);
        }

        return SignInOutcome.Ok(await tokens.IssueAsync(user, identity.Provider, cancellationToken: cancellationToken));
    }

    /// <summary><c>POST /auth/pending/link</c>: the user proved an existing account; the pending identity joins it.</summary>
    public async Task<SignInOutcome> LinkPendingAsync(SpinePendingRequest request, SpineCaller caller, CancellationToken cancellationToken)
    {
        if (await TakePendingAsync(request.Ticket, cancellationToken) is not { } identity) return SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant);

        var (user, refusal) = await ResolveAsync(identity, caller.UserId, cancellationToken);
        return user is null ? refusal! : SignInOutcome.Ok(await tokens.IssueAsync(user, caller.Provider, cancellationToken: cancellationToken));
    }

    /// <summary><c>POST /auth/link/{provider}</c>: an ID token from the provider's sheet joins the signed-in user.</summary>
    public async Task<SignInOutcome> LinkWithIdTokenAsync(string providerName, SpineIdTokenRequest request, SpineCaller caller, CancellationToken cancellationToken)
    {
        var (user, refusal) = await SignInUserAsync(providerName, request, null, false, cancellationToken, linkTo: caller.UserId);
        return user is null ? refusal! : SignInOutcome.Ok(await tokens.IssueAsync(user, caller.Provider, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// <c>DELETE /auth/link/{provider}</c>: removes a provider from the signed-in user, never the last way to sign in.
    /// With <paramref name="subject"/> (<c>DELETE /auth/link/{provider}/{subject}</c>), only that login, such as one passkey.
    /// </summary>
    public async Task<SignInOutcome> UnlinkAsync(string providerName, SpineCaller caller, CancellationToken cancellationToken, string? subject = null)
    {
        if (await users.FindByIdAsync(caller.UserId, cancellationToken) is not { } user) return SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant);

        var logins = await users.GetLoginsAsync(user.Id, cancellationToken);
        var removing = logins.Where(l => l.Provider == providerName && (subject is null || l.Subject == subject)).ToList();
        if (removing.Count == 0)
            return SignInOutcome.Refused(404, SpineAuthErrors.InvalidRequest, subject is null ? $"The account has no {providerName} login." : $"The account has no such {providerName} login.");
        if (removing.Count == logins.Count)
            return SignInOutcome.Refused(409, SpineAuthErrors.LastLogin, "This is the account's last way to sign in; add another first.");

        try
        {
            foreach (var login in removing)
            {
                await users.DeleteLoginAsync(login, cancellationToken);
                if (login is { Provider: "apple", ProviderRefreshToken: { } appleToken, ProviderClientId: { } clientId } && apple is not null)
                    await apple.RevokeAsync(appleToken, clientId, cancellationToken);
            }
        }
        catch (NotSupportedException e)
        {
            return SignInOutcome.Refused(501, SpineAuthErrors.InvalidRequest, e.Message);
        }

        // A password is a login and a hash; without the login the hash would still sign in.
        if (providerName == SpineAuthDefaults.PasswordProvider)
            user = await ChangeAsync(user.Id, u => (u.PasswordHash = null) is null, cancellationToken) ?? user;

        logger.LogInformation("Spine sign-in: user {User} removed their {Provider} login.", user.Id, providerName);
        return SignInOutcome.Ok(await tokens.IssueAsync(user, caller.Provider, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// <c>POST /auth/link/password</c>: email and password become a way into the signed-in user, who signed in some other
    /// way. An email other than the account's own replaces it, unverified; with
    /// <see cref="SpineEmailPasswordOptions.RequireVerifiedEmail"/> a code goes to it first, as on sign-up.
    /// </summary>
    public async Task<SignInOutcome> AddPasswordAsync(SpinePasswordSignIn request, SpineCaller caller, CancellationToken cancellationToken)
    {
        if (options.EmailPasswordOptions is not { } password) return SignInOutcome.Refused(404, SpineAuthErrors.UnknownProvider);
        if (Email(request.Email) is not { } address) return SignInOutcome.Refused(400, SpineAuthErrors.InvalidRequest, "email is not an email address.");
        if (PasswordProblem(request.Password) is { } weak) return weak;

        if (await users.FindByIdAsync(caller.UserId, cancellationToken) is not { } user) return SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant);
        if ((await users.GetLoginsAsync(user.Id, cancellationToken)).Any(l => l.Provider == SpineAuthDefaults.PasswordProvider))
            return SignInOutcome.Refused(409, SpineAuthErrors.InvalidRequest, "The account already has a password.");

        // The caller is signed in, so saying the email is someone else's tells them nothing a sign-up would not.
        if (!string.Equals(user.Email, address, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (!await users.ChangeEmailAsync(user.Id, address, cancellationToken)) return SignInOutcome.Refused(409, SpineAuthErrors.EmailTaken);
            }
            catch (NotSupportedException e)
            {
                return SignInOutcome.Refused(501, SpineAuthErrors.InvalidRequest, e.Message);
            }
        }

        user = await ChangeAsync(user.Id, u =>
        {
            u.PasswordHash = _hasher.HashPassword(u, request.Password);
            u.FailedSignIns = 0;
            u.LockedUntil = null;
            return true;
        }, cancellationToken);
        if (user is null) return SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant);

        await users.SaveLoginAsync(new SpineAuthLogin(SpineAuthDefaults.PasswordProvider, user.Id, user.Id)
        {
            LinkedAt = time.GetUtcNow(),
            Email = user.Email,
            DisplayName = user.DisplayName,
        }, cancellationToken);
        logger.LogInformation("Spine sign-in: user {User} added email and password.", user.Id);

        if (password.RequireVerifiedEmail && !user.EmailVerified)
        {
            await SendCodeAsync(SpineAuthEmailKind.VerifyEmail, user, cancellationToken);
            return SignInOutcome.Accepted;
        }

        return SignInOutcome.Ok(await tokens.IssueAsync(user, caller.Provider, cancellationToken: cancellationToken));
    }

    /// <summary><c>POST /auth/link/ticket</c>: a one-time ticket that lets a browser sign-in link to the signed-in user.</summary>
    public async Task<SpineLinkTicket> CreateLinkTicketAsync(SpineCaller caller, CancellationToken cancellationToken)
    {
        var ticket = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = time.GetUtcNow() + options.LinkTicketLifetime;
        await store.SaveOneTimeAsync(LinkKey(ticket), JsonSerializer.Serialize(caller), expiresAt, cancellationToken);
        return new SpineLinkTicket(ticket, expiresAt);
    }

    public async Task<SpineCaller?> TakeLinkTicketAsync(string ticket, CancellationToken cancellationToken) =>
        await store.TakeOneTimeAsync(LinkKey(ticket), time.GetUtcNow(), cancellationToken) is { } saved
            ? JsonSerializer.Deserialize<SpineCaller>(saved)
            : null;

    private static string LinkKey(string ticket) => SpineTokenService.Hash($"link|{ticket}");

    private async Task<Identity?> TakePendingAsync(string? ticket, CancellationToken cancellationToken) =>
        ticket is { Length: > 0 } && await store.TakeOneTimeAsync(PendingKey(ticket), time.GetUtcNow(), cancellationToken) is { } saved
            ? JsonSerializer.Deserialize<Identity>(saved)
            : null;

    private static string PendingKey(string ticket) => SpineTokenService.Hash($"pending|{ticket}");

    /// <summary>A provider identity as a validated ID token describes it; kept as is while it is pending.</summary>
    internal sealed record Identity(
        string Provider, string Subject, string? VerifiedEmail, string? Email, string? Name, string? GivenName, string? FamilyName,
        string? Picture, string? Organization, bool ReportsOrganization, string? ProviderRefreshToken, string? ProviderClientId);

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

        await users.SaveLoginAsync(new SpineAuthLogin(SpineAuthDefaults.PasswordProvider, user.Id, user.Id)
        {
            LinkedAt = user.CreatedAt,
            Email = address,
            DisplayName = user.DisplayName,
        }, cancellationToken);

        if (!password.RequireVerifiedEmail)
            return SignInOutcome.Ok(await PasswordSignedInAsync(user, cancellationToken));

        await SendCodeAsync(SpineAuthEmailKind.VerifyEmail, user, cancellationToken);
        return SignInOutcome.Accepted;
    }

    // A password sign-in (a password, an emailed code, a reset) is the password login's use.
    private async Task<SpineTokens> PasswordSignedInAsync(SpineAuthUser user, CancellationToken cancellationToken)
    {
        if ((await users.GetLoginsAsync(user.Id, cancellationToken)).FirstOrDefault(l => l.Provider == SpineAuthDefaults.PasswordProvider) is { } login)
            await users.SaveLoginAsync(login with { LastUsedAt = time.GetUtcNow() }, cancellationToken);

        return await tokens.IssueAsync(user, SpineAuthDefaults.PasswordProvider, cancellationToken: cancellationToken);
    }

    public async Task<SignInOutcome> VerifyEmailAsync(SpineEmailCode request, CancellationToken cancellationToken)
    {
        if (await TakeCodeAsync(SpineAuthEmailKind.VerifyEmail, request.Email, request.Code, cancellationToken) is not { } userId
            || await ChangeAsync(userId, u => !u.EmailVerified && (u.EmailVerified = true), cancellationToken) is not { } user)
        {
            return SignInOutcome.Refused(400, SpineAuthErrors.InvalidCode);
        }

        return SignInOutcome.Ok(await PasswordSignedInAsync(user, cancellationToken));
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

            return SignInOutcome.Ok(await PasswordSignedInAsync(user, cancellationToken));
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

        // A reset adds a password to an account that had none; one it had keeps when it was added.
        if (!(await users.GetLoginsAsync(user.Id, cancellationToken)).Any(l => l.Provider == SpineAuthDefaults.PasswordProvider))
        {
            await users.SaveLoginAsync(new SpineAuthLogin(SpineAuthDefaults.PasswordProvider, user.Id, user.Id)
            {
                LinkedAt = time.GetUtcNow(),
                Email = user.Email,
                DisplayName = user.DisplayName,
            }, cancellationToken);
        }
        await store.RevokeUserAsync(user.Id, cancellationToken);

        return SignInOutcome.Ok(await PasswordSignedInAsync(user, cancellationToken));
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
