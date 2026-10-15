using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>What an accepted ID token says about the user.</summary>
internal sealed record ValidatedIdToken(string Subject, string Audience, string? Email, bool EmailVerified, string? Name);

/// <summary>Checks ID tokens from the providers' native sheets: signature from the provider's JWKS, issuer, audience, expiry and nonce.</summary>
internal sealed class IdTokenValidator(ISpineAuthStore store, IHttpClientFactory http, TimeProvider time, SpineAuthOptions options)
{
    internal const string HttpClientName = "Plugin.Maui.Spine.Server.Authentication";

    private readonly ConcurrentDictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> _configurations = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonWebTokenHandler _handler = new();

    public async Task<SpineNonce> CreateNonceAsync(CancellationToken cancellationToken)
    {
        var nonce = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));
        var expiresAt = time.GetUtcNow() + options.NonceLifetime;
        await store.SaveOneTimeAsync(NonceKey(nonce), "nonce", expiresAt, cancellationToken);
        return new SpineNonce(nonce, expiresAt);
    }

    /// <returns>The token's facts, or why it was refused: a short reason safe to show a developer.</returns>
    public async Task<(ValidatedIdToken? Token, string? Problem)> ValidateAsync(
        string name, SpineIdTokenProviderOptions provider, SpineIdTokenRequest request, CancellationToken cancellationToken)
    {
        var configuration = _configurations.GetOrAdd(name, _ => provider.Configuration ?? Discovery(provider.Authority!));

        var result = await ValidateWithAsync(await configuration.GetConfigurationAsync(cancellationToken));

        // A provider that rotated its keys since the last fetch: fetch again once, then decide.
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            configuration.RequestRefresh();
            result = await ValidateWithAsync(await configuration.GetConfigurationAsync(cancellationToken));
        }

        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
            return (null, Reason(result.Exception));

        if (!await NonceIsGoodAsync(provider, request.Nonce, token, cancellationToken))
            return (null, "the nonce is missing, wrong, used or expired");

        if (token.Subject is not { Length: > 0 } subject)
            return (null, "the token has no sub");

        var audience = token.Audiences.First(a => provider.ClientIds.Contains(a));
        return (new ValidatedIdToken(subject, audience, Claim(token, "email"), IsTrue(token, "email_verified"), Claim(token, "name")), null);

        async Task<TokenValidationResult> ValidateWithAsync(OpenIdConnectConfiguration config)
        {
            var parameters = new TokenValidationParameters
            {
                ValidIssuers = provider.ValidIssuers.Count > 0 ? provider.ValidIssuers : [config.Issuer],
                ValidAudiences = provider.ClientIds,
                IssuerSigningKeys = config.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(2),
                ValidateLifetime = true,
                RequireExpirationTime = true,
            };

            if (provider is SpineMicrosoftOptions microsoft)
                parameters.IssuerValidator = (issuer, token, _) => MicrosoftIssuer(microsoft, issuer, (JsonWebToken)token);

            return await _handler.ValidateTokenAsync(request.IdToken, parameters);
        }
    }

    private async Task<bool> NonceIsGoodAsync(SpineIdTokenProviderOptions provider, string? raw, JsonWebToken token, CancellationToken cancellationToken)
    {
        if (!provider.RequireNonce && raw is null) return true;
        if (raw is not { Length: > 0 } || Claim(token, "nonce") is not { } claimed) return false;

        var expected = provider.HashNonce ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw))) : raw;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(claimed))) return false;

        return await store.TakeOneTimeAsync(NonceKey(raw), time.GetUtcNow(), cancellationToken) is not null;
    }

    private IConfigurationManager<OpenIdConnectConfiguration> Discovery(string authority) =>
        new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(http.CreateClient(HttpClientName)) { RequireHttps = true });

    // "common" signs with the issuer of the user's own tenant, so the issuer is checked against the token's tid.
    private static string MicrosoftIssuer(SpineMicrosoftOptions microsoft, string issuer, JsonWebToken token)
    {
        var tenant = Claim(token, "tid");
        if (microsoft.AcceptsTenant(tenant) && issuer == $"https://login.microsoftonline.com/{tenant}/v2.0") return issuer;
        throw new SecurityTokenInvalidIssuerException($"The issuer '{issuer}' is not a tenant this app accepts.");
    }

    private static string NonceKey(string nonce) => SpineTokenService.Hash($"nonce|{nonce}");

    private static string? Claim(JsonWebToken token, string name) =>
        token.TryGetPayloadValue<string>(name, out var value) && value is { Length: > 0 } ? value : null;

    // Apple sends email_verified as the string "true", Google as a boolean.
    private static bool IsTrue(JsonWebToken token, string name) =>
        (token.TryGetPayloadValue<bool>(name, out var flag) && flag)
        || (token.TryGetPayloadValue<string>(name, out var text) && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase));

    private static string Reason(Exception? e) => e switch
    {
        SecurityTokenExpiredException => "the token has expired",
        SecurityTokenInvalidAudienceException => "the token is for another client id",
        SecurityTokenInvalidIssuerException => "the token is from another issuer",
        SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException => "the signature does not match the provider's keys",
        SecurityTokenMalformedException or ArgumentException => "the token is not a JWT",
        _ => "the token is not valid",
    };
}

/// <summary>Apple's token endpoints: the one-time code traded for a refresh token, and that token revoked on account deletion (TN3194).</summary>
internal sealed class AppleTokenClient(SpineAppleOptions apple, IHttpClientFactory http, TimeProvider time, ILogger logger)
{
    private const string Apple = "https://appleid.apple.com";

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    // One key for the client's life: IdentityModel caches a signer per key id, and a disposed key in that cache breaks the next secret.
    private readonly Lazy<ECDsaSecurityKey> _key = new(() =>
    {
        var key = ECDsa.Create();
        key.ImportFromPem(apple.PrivateKey);
        return new ECDsaSecurityKey(key) { KeyId = apple.KeyId };
    });

    /// <returns>Apple's refresh token, or <see langword="null"/> when Apple refused or could not be reached (logged); the sign-in goes on without it.</returns>
    public async Task<string?> ExchangeAsync(string code, string clientId, CancellationToken cancellationToken)
    {
        try
        {
            return await TryExchangeAsync(code, clientId, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "Sign in with Apple: the authorization code could not be traded; this user's Apple tokens cannot be revoked on deletion.");
            return null;
        }
    }

    private async Task<string?> TryExchangeAsync(string code, string clientId, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(IdTokenValidator.HttpClientName).PostAsync($"{Apple}/auth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = ClientSecret(clientId),
            ["code"] = code,
            ["grant_type"] = "authorization_code",
        }), cancellationToken);

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Sign in with Apple: the authorization code was refused ({Status}: {Answer}); this user's Apple tokens cannot be revoked on deletion.",
                (int)response.StatusCode, text);
            return null;
        }

        using var json = JsonDocument.Parse(text);
        return json.RootElement.TryGetProperty("refresh_token", out var token) ? token.GetString() : null;
    }

    public async Task<bool> RevokeAsync(string refreshToken, string clientId, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(IdTokenValidator.HttpClientName).PostAsync($"{Apple}/auth/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = ClientSecret(clientId),
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
        }), cancellationToken);

        if (response.IsSuccessStatusCode) return true;

        logger.LogError("Sign in with Apple: revoking a token failed ({Status}: {Answer}).",
            (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        return false;
    }

    internal string ClientSecret(string clientId)
    {
        var now = time.GetUtcNow();

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = apple.TeamId,
            Audience = Apple,
            Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", clientId)]),
            IssuedAt = now.UtcDateTime,
            Expires = now.AddMinutes(5).UtcDateTime,
            SigningCredentials = new SigningCredentials(_key.Value, SecurityAlgorithms.EcdsaSha256),
        });
    }
}
