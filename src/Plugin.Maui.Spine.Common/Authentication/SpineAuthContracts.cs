using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>A signed-in user as the app sees them.</summary>
/// <param name="Id">The user's id on the app's server: the access token's <c>sub</c>.</param>
/// <param name="Email">The email, when the user has one.</param>
/// <param name="DisplayName">The name to show, when there is one.</param>
/// <param name="Provider">How the user signed in this time: <c>apple</c>, <c>google</c>, <c>password</c>, …</param>
/// <param name="PictureUrl">
/// A public https address of the user's picture, when a provider gave one (Google's <c>picture</c>). Microsoft's
/// picture has no address; <c>ISpineAuth.GetPictureAsync</c> in the app fetches either.
/// </param>
/// <param name="GivenName">The first name, when a provider gave it: Apple's sheet the first time, Google's and Microsoft's tokens.</param>
/// <param name="FamilyName">The last name, as <paramref name="GivenName"/>.</param>
/// <param name="Organization">
/// The organization the account belongs to: a Microsoft work or school account's tenant id, a Google Workspace
/// account's domain. Null for personal accounts and the other providers.
/// </param>
/// <param name="SignInMethods">Every way the user can sign in to this account, such as <c>apple</c>, <c>google</c> and <c>password</c>.</param>
/// <param name="Logins">The account's logins with what each provider says about it: one per provider identity, in the order they were linked.</param>
public sealed record SpineUser(
    string Id,
    string? Email,
    string? DisplayName,
    string Provider,
    string? PictureUrl = null,
    string? GivenName = null,
    string? FamilyName = null,
    string? Organization = null,
    IReadOnlyList<string>? SignInMethods = null,
    IReadOnlyList<SpineLoginInfo>? Logins = null);

/// <summary>One way the user signs in to the account, as the app can show it on an accounts page.</summary>
/// <param name="Provider">The provider: <c>apple</c>, <c>google</c>, <c>microsoft</c>, <c>password</c>, …</param>
/// <param name="Email">The email that provider gave, when it gave one; not necessarily verified, and not the account's <see cref="SpineUser.Email"/>.</param>
/// <param name="DisplayName">The name that provider gave.</param>
/// <param name="LinkedAt">When the login was added to the account; null for logins made before Spine kept it.</param>
/// <param name="Subject">
/// The provider's id for this login (a passkey's credential id), so one of several logins of a provider can be removed:
/// <c>ISpineAuth.UnlinkAsync(provider, subject)</c>.
/// </param>
public sealed record SpineLoginInfo(string Provider, string? Email = null, string? DisplayName = null, DateTimeOffset? LinkedAt = null, string? Subject = null);

/// <summary>
/// The start of a passkey ceremony: <c>POST /auth/passkey/register/options</c> or <c>/signin/options</c>. The app hands
/// <paramref name="Options"/> to the platform and posts its answer back with <paramref name="ChallengeId"/>.
/// </summary>
/// <param name="ChallengeId">Names this ceremony on the server; it works once, for a few minutes.</param>
/// <param name="Options">WebAuthn's <c>PublicKeyCredentialCreationOptions</c> or <c>…RequestOptions</c>, as JSON.</param>
public sealed record SpinePasskeyChallenge(string ChallengeId, string Options);

/// <summary>A new account made with a passkey: the body of <c>POST /auth/passkey/register/options</c> without an access token.</summary>
/// <param name="DisplayName">The name the passkey and the account carry.</param>
/// <param name="Email">An email to show; kept unverified, not as the account's email, until a code verifies it.</param>
public sealed record SpinePasskeyNewAccount(string? DisplayName, string? Email);

/// <summary>What the platform's passkey sheet answered: the body of <c>POST /auth/passkey/register</c> or <c>/signin</c>.</summary>
/// <param name="ChallengeId">The <see cref="SpinePasskeyChallenge.ChallengeId"/>.</param>
/// <param name="Credential">The WebAuthn credential as JSON (<c>PublicKeyCredential.toJSON()</c>'s shape).</param>
public sealed record SpinePasskeyResponse(string ChallengeId, string Credential);

/// <summary>
/// A sign-in with an identity the server does not know, while the server asks before it makes an account
/// (<c>AskBeforeNewAccount</c>). The app asks the user, then either creates the account (<c>POST /auth/pending/create</c>)
/// or links the identity to the account the user proves with another way to sign in (<c>POST /auth/pending/link</c>).
/// </summary>
/// <param name="Ticket">The one-time ticket for the next step.</param>
/// <param name="Provider">The provider the user signed in with.</param>
/// <param name="Email">The email the provider vouches for, when it does.</param>
/// <param name="DisplayName">The name the provider gave.</param>
/// <param name="ExpiresAt">When the ticket stops working.</param>
public sealed record SpinePendingIdentity(string Ticket, string Provider, string? Email, string? DisplayName, DateTimeOffset ExpiresAt);

/// <summary>The body of <c>POST /auth/pending/create</c> and <c>POST /auth/pending/link</c>.</summary>
/// <param name="Ticket">The pending identity's ticket.</param>
public sealed record SpinePendingRequest(string Ticket);

/// <summary>
/// A one-time ticket from <c>POST /auth/link/ticket</c> that a browser sign-in carries (<c>link=</c> on
/// <c>/auth/{provider}/start</c>), so the server links the identity to the signed-in user instead of signing in.
/// </summary>
/// <param name="Ticket">The ticket.</param>
/// <param name="ExpiresAt">When it stops working.</param>
public sealed record SpineLinkTicket(string Ticket, DateTimeOffset ExpiresAt);

/// <summary>What a successful sign-in or refresh answers.</summary>
/// <param name="AccessToken">A short-lived JWT for <c>Authorization: Bearer</c>.</param>
/// <param name="ExpiresAt">When <paramref name="AccessToken"/> stops working.</param>
/// <param name="RefreshToken">
/// A single-use token for the next pair. Presenting it twice signs out every device that got its tokens from the
/// same sign-in.
/// </param>
/// <param name="User">Who signed in.</param>
public sealed record SpineTokens(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken, SpineUser User);

/// <summary>A one-time nonce for a native sign-in request.</summary>
/// <param name="Nonce">Give the provider this (Apple: its SHA-256 as hex), then send it back unchanged with the ID token.</param>
/// <param name="ExpiresAt">When the server forgets it.</param>
public sealed record SpineNonce(string Nonce, DateTimeOffset ExpiresAt);

/// <summary>The body of <c>POST /auth/{provider}/token</c>.</summary>
/// <param name="IdToken">The ID token from the platform's sign-in sheet.</param>
/// <param name="Nonce">The raw nonce from <c>GET /auth/nonce</c>.</param>
/// <param name="AuthorizationCode">Apple's one-time code, which the server keeps to revoke Apple's tokens when the account is deleted.</param>
/// <param name="DisplayName">The name the sheet returned. Apple sends it only on the first sign-in.</param>
/// <param name="GivenName">The first name the sheet returned, as <paramref name="DisplayName"/>.</param>
/// <param name="FamilyName">The last name the sheet returned, as <paramref name="DisplayName"/>.</param>
public sealed record SpineIdTokenRequest(
    string IdToken, string? Nonce = null, string? AuthorizationCode = null, string? DisplayName = null, string? GivenName = null, string? FamilyName = null);

/// <summary>The body of <c>POST /auth/token</c> and <c>POST /auth/revoke</c>.</summary>
/// <param name="RefreshToken">The refresh token to trade or revoke.</param>
public sealed record SpineRefreshRequest(string RefreshToken);

/// <summary>The body of <c>POST /auth/token</c> after a browser sign-in: the one-time code the server redirected back with.</summary>
/// <param name="Code">The <c>code</c> from the redirect.</param>
/// <param name="CodeVerifier">The PKCE verifier whose SHA-256 was sent as <c>code_challenge</c> to <c>/auth/{provider}/start</c>.</param>
/// <param name="RedirectUri">The same <c>redirect_uri</c> that was sent to <c>/auth/{provider}/start</c>.</param>
public sealed record SpineCodeExchange(string Code, string CodeVerifier, string RedirectUri);

/// <summary>The body of <c>POST /auth/password/signin</c>.</summary>
/// <param name="Email">The email.</param>
/// <param name="Password">The password.</param>
public sealed record SpinePasswordSignIn(string Email, string Password);

/// <summary>The body of <c>POST /auth/password/signup</c>.</summary>
/// <param name="Email">The email.</param>
/// <param name="Password">The password.</param>
/// <param name="DisplayName">The name to show.</param>
public sealed record SpinePasswordSignUp(string Email, string Password, string? DisplayName = null);

/// <summary>The body of <c>POST /auth/password/forgot</c> and <c>POST /auth/password/resend</c>.</summary>
/// <param name="Email">The email.</param>
public sealed record SpineEmailRequest(string Email);

/// <summary>The body of <c>POST /auth/password/verify</c>.</summary>
/// <param name="Email">The email.</param>
/// <param name="Code">The code from the email.</param>
public sealed record SpineEmailCode(string Email, string Code);

/// <summary>The body of <c>POST /auth/password/reset</c>.</summary>
/// <param name="Email">The email.</param>
/// <param name="Code">The code from the email.</param>
/// <param name="Password">The new password.</param>
public sealed record SpinePasswordReset(string Email, string Code, string Password);

/// <summary>A refusal from the sign-in endpoints.</summary>
/// <param name="Error">One of <see cref="SpineAuthErrors"/>.</param>
/// <param name="Detail">More for a developer, when it reveals nothing about other accounts.</param>
public sealed record SpineAuthProblem(string Error, string? Detail = null);

/// <summary>The <see cref="SpineAuthProblem.Error"/> values.</summary>
public static class SpineAuthErrors
{
    /// <summary>The body is missing or malformed.</summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>The provider is not configured on the server.</summary>
    public const string UnknownProvider = "unknown_provider";

    /// <summary>The ID token failed validation: signature, issuer, audience, expiry or nonce.</summary>
    public const string InvalidToken = "invalid_token";

    /// <summary>The refresh token is unknown, expired, revoked or reused; or a browser sign-in's code is unknown, used, expired or does not match its verifier and redirect.</summary>
    public const string InvalidGrant = "invalid_grant";

    /// <summary>The email or the password is wrong, or the account is locked after too many wrong passwords. Which one is not said.</summary>
    public const string InvalidCredentials = "invalid_credentials";

    /// <summary>The email has not been verified yet. A new code has been sent.</summary>
    public const string EmailNotVerified = "email_not_verified";

    /// <summary>An account with that email exists (only when sign-up does not verify email).</summary>
    public const string EmailTaken = "email_taken";

    /// <summary>The password is too short.</summary>
    public const string WeakPassword = "weak_password";

    /// <summary>The emailed code is wrong or has expired.</summary>
    public const string InvalidCode = "invalid_code";

    /// <summary>The provider's discovery document or token endpoint could not be reached.</summary>
    public const string ProviderUnavailable = "provider_unavailable";

    /// <summary>Sign-up is turned off on the server.</summary>
    public const string SignUpClosed = "signup_closed";

    /// <summary>The identity to link belongs to another account. Nothing moves between accounts; sign in with it there and remove it first.</summary>
    public const string IdentityInUse = "identity_in_use";

    /// <summary>The login to remove is the account's last way to sign in.</summary>
    public const string LastLogin = "last_login";

    /// <summary>The passkey is not on any account here: removed from it, or the account was deleted.</summary>
    public const string UnknownPasskey = "unknown_passkey";
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SpineUser))]
[JsonSerializable(typeof(SpineTokens))]
[JsonSerializable(typeof(SpineNonce))]
[JsonSerializable(typeof(SpineIdTokenRequest))]
[JsonSerializable(typeof(SpineRefreshRequest))]
[JsonSerializable(typeof(SpineCodeExchange))]
[JsonSerializable(typeof(SpinePasswordSignIn))]
[JsonSerializable(typeof(SpinePasswordSignUp))]
[JsonSerializable(typeof(SpineEmailRequest))]
[JsonSerializable(typeof(SpineEmailCode))]
[JsonSerializable(typeof(SpinePasswordReset))]
[JsonSerializable(typeof(SpineAuthProblem))]
[JsonSerializable(typeof(SpinePendingIdentity))]
[JsonSerializable(typeof(SpinePendingRequest))]
[JsonSerializable(typeof(SpineLinkTicket))]
[JsonSerializable(typeof(SpinePasskeyChallenge))]
[JsonSerializable(typeof(SpinePasskeyNewAccount))]
[JsonSerializable(typeof(SpinePasskeyResponse))]
internal sealed partial class SpineAuthJsonContext : JsonSerializerContext;

/// <summary>The wire format of the sign-in endpoints, shared by the app and the server: camelCase, nulls left out.</summary>
public static class SpineAuthJson
{
    /// <summary>Options that serialize every contract in this namespace without reflection.</summary>
    public static JsonSerializerOptions Options => SpineAuthJsonContext.Default.Options;
}
