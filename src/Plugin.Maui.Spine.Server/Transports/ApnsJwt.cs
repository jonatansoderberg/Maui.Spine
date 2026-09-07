using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// The provider token APNs authenticates with: an ES256 JWT signed with the <c>.p8</c> key.
/// </summary>
/// <remarks>
/// Apple rejects a token younger than 20 minutes if it is refreshed too eagerly, and one older than
/// 60 minutes outright, so the same token is reused for <see cref="Lifetime"/> and then replaced.
/// </remarks>
/// <param name="options">The team, key and private key to sign with.</param>
/// <param name="timeProvider">The clock the token is stamped and aged against.</param>
internal sealed class ApnsJwt(ApplePushOptions options, TimeProvider timeProvider)
{
    /// <summary>How long one token is reused. Apple's window is 20–60 minutes.</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(50);

    private readonly Lock _gate = new();
    private string? _token;
    private DateTimeOffset _issuedAt;

    /// <summary>How many tokens have been minted. Lets a test see that reuse actually happens.</summary>
    internal int Issued { get; private set; }

    /// <summary>The current token, minting a new one when the old one has aged out.</summary>
    internal string Token
    {
        get
        {
            var now = timeProvider.GetUtcNow();

            lock (_gate)
            {
                if (_token is not null && now - _issuedAt < Lifetime) return _token;

                _token = Mint(now);
                _issuedAt = now;
                Issued++;
                return _token;
            }
        }
    }

    private string Mint(DateTimeOffset now)
    {
        var header = Segment(new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["kid"] = options.KeyId!,
        });

        var claims = Segment(new Dictionary<string, object>
        {
            ["iss"] = options.TeamId!,
            ["iat"] = now.ToUnixTimeSeconds(),
        });

        var signingInput = $"{header}.{claims}";

        using var key = ECDsa.Create();
        key.ImportFromPem(options.PrivateKey);

        // JWT wants the raw r||s pair, not the DER sequence SignData writes by default.
        var signature = key.SignData(
            Encoding.UTF8.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Segment(Dictionary<string, object> value) =>
        Base64Url(JsonSerializer.SerializeToUtf8Bytes(value));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
