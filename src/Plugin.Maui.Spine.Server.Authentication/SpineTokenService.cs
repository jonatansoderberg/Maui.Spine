using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>Spine's own tokens: ES256 access tokens and rotating refresh tokens.</summary>
internal sealed class SpineTokenService
{
    private readonly SpineAuthOptions _options;
    private readonly ISpineAuthStore _store;
    private readonly IUserResolver _users;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ECDsaSecurityKey _signing;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public SpineTokenService(SpineAuthOptions options, ISpineAuthStore store, IUserResolver users, TimeProvider time, ILogger logger)
    {
        _options = options;
        _store = store;
        _users = users;
        _time = time;
        _logger = logger;

        _signing = KeyFrom(options.SigningKey!, "SigningKey");
        VerificationKeys = [_signing, .. options.PreviousSigningKeys.Select((pem, i) => KeyFrom(pem, $"PreviousSigningKeys[{i}]"))];
    }

    /// <summary>The keys access tokens are checked with: the signing key and the previous ones.</summary>
    public IReadOnlyList<ECDsaSecurityKey> VerificationKeys { get; }

    public async Task<SpineTokens> IssueAsync(SpineAuthUser user, string provider, string? familyId = null, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var expiresAt = now + _options.AccessTokenLifetime;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            [SpineAuthDefaults.ProviderClaim] = provider,
        };
        if (user.Email is { } email) claims[JwtRegisteredClaimNames.Email] = email;
        if (user.DisplayName is { } name) claims[JwtRegisteredClaimNames.Name] = name;

        var access = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.EffectiveAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(_signing, SecurityAlgorithms.EcdsaSha256),
        });

        var refresh = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        await _store.AddRefreshAsync(new SpineRefreshToken(
            Hash(refresh), familyId ?? Guid.NewGuid().ToString("N"), user.Id, provider, now + _options.RefreshTokenLifetime), cancellationToken);

        return new SpineTokens(access, expiresAt, refresh, user.ToContract(provider, await _users.GetLoginsAsync(user.Id, cancellationToken)));
    }

    /// <summary>Trades a refresh token for a new pair. A token presented twice revokes its whole family.</summary>
    /// <returns>The new pair, or <see langword="null"/> when the token is unknown, expired, revoked or reused.</returns>
    public async Task<SpineTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(refreshToken);
        var now = _time.GetUtcNow();

        if (await _store.FindRefreshAsync(hash, cancellationToken) is not { } stored) return null;

        if (stored.UsedAt is not null || !await _store.TryUseRefreshAsync(hash, now, cancellationToken))
        {
            // A client whose answer was lost presents the same token again within moments; that is not reuse.
            var usedAt = stored.UsedAt ?? (await _store.FindRefreshAsync(hash, cancellationToken))?.UsedAt;
            if (usedAt is { } used && now - used <= _options.RefreshReuseGrace)
            {
                _logger.LogInformation("Spine sign-in: a refresh token was presented again {Seconds:0.#} s after its use; within the grace, a new pair is issued.",
                    (now - used).TotalSeconds);
            }
            else
            {
                // Rotation means a token is presented once. A second presentation is either a stolen copy or a stolen
                // original, and there is no telling which, so every device of that sign-in starts over.
                await _store.RevokeFamilyAsync(stored.UserId, stored.FamilyId, cancellationToken);
                _logger.LogWarning("Spine sign-in: a used refresh token was presented again; family {Family} of user {User} is revoked.",
                    stored.FamilyId, stored.UserId);
                return null;
            }
        }

        if (stored.ExpiresAt <= now) return null;
        if (await _store.IsFamilyRevokedAsync(stored.UserId, stored.FamilyId, cancellationToken)) return null;
        if (await _users.FindByIdAsync(stored.UserId, cancellationToken) is not { } user) return null;

        return await IssueAsync(user, stored.Provider, stored.FamilyId, cancellationToken);
    }

    /// <summary>Revokes the family of a refresh token, if the token is known.</summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (await _store.FindRefreshAsync(Hash(refreshToken), cancellationToken) is { } stored)
            await _store.RevokeFamilyAsync(stored.UserId, stored.FamilyId, cancellationToken);
    }

    /// <summary>The public keys as a JWK set, for anyone else who checks Spine's access tokens.</summary>
    public string Jwks()
    {
        var keys = new JsonArray();
        foreach (var key in VerificationKeys)
        {
            var parameters = key.ECDsa.ExportParameters(false);
            keys.Add(new JsonObject
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64UrlEncoder.Encode(parameters.Q.X),
                ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y),
                ["kid"] = key.KeyId,
                ["use"] = "sig",
                ["alg"] = SecurityAlgorithms.EcdsaSha256,
            });
        }

        return new JsonObject { ["keys"] = keys }.ToJsonString();
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = _options.Issuer,
        ValidAudience = _options.EffectiveAudience,
        IssuerSigningKeys = VerificationKeys,
        ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Name,
    };

    internal static string Hash(string value) => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ECDsaSecurityKey KeyFrom(string pem, string name)
    {
        var ecdsa = ECDsa.Create();

        try
        {
            ecdsa.ImportFromPem(pem);
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            throw new InvalidOperationException($"AddSpineAuthentication: {name} is not an EC key in PEM: {e.Message}", e);
        }

        if (ecdsa.KeySize != 256)
            throw new InvalidOperationException($"AddSpineAuthentication: {name} is a {ecdsa.KeySize}-bit EC key; ES256 needs P-256.");

        // The key id is a digest of the public key, so the same key always has the same id in every process.
        var id = Base64UrlEncoder.Encode(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo()))[..16];
        return new ECDsaSecurityKey(ecdsa) { KeyId = id };
    }
}
