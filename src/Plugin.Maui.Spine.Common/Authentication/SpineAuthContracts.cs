using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>A signed-in user as the app sees them.</summary>
/// <param name="Id">The user's id on the app's server: the access token's <c>sub</c>.</param>
/// <param name="Email">The email, when the user has one.</param>
/// <param name="DisplayName">The name to show, when there is one.</param>
/// <param name="Provider">How the user signed in this time: <c>apple</c>, <c>google</c>, <c>password</c>, …</param>
public sealed record SpineUser(string Id, string? Email, string? DisplayName, string Provider);

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
public sealed record SpineIdTokenRequest(string IdToken, string? Nonce = null, string? AuthorizationCode = null, string? DisplayName = null);

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
internal sealed partial class SpineAuthJsonContext : JsonSerializerContext;

/// <summary>The wire format of the sign-in endpoints, shared by the app and the server: camelCase, nulls left out.</summary>
public static class SpineAuthJson
{
    /// <summary>Options that serialize every contract in this namespace without reflection.</summary>
    public static JsonSerializerOptions Options => SpineAuthJsonContext.Default.Options;
}
