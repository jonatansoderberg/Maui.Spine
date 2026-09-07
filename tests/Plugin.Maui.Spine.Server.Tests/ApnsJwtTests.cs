using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class ApnsJwtTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static (ApplePushOptions Options, ECDsa Key) NewOptions()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (new ApplePushOptions
        {
            TeamId = "TEAM123456",
            KeyId = "KEY1234567",
            BundleId = "com.companyname.orientera",
            PrivateKey = key.ExportPkcs8PrivateKeyPem(),
        }, key);
    }

    private static byte[] Base64Url(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    [Fact]
    public void The_header_names_es256_and_the_key()
    {
        var (options, key) = NewOptions();
        using var _ = key;

        var token = new ApnsJwt(options, new FakeTimeProvider(Now)).Token;
        var header = JsonDocument.Parse(Base64Url(token.Split('.')[0])).RootElement;

        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        Assert.Equal("KEY1234567", header.GetProperty("kid").GetString());
    }

    [Fact]
    public void The_claims_are_the_team_and_the_issue_time()
    {
        var (options, key) = NewOptions();
        using var _ = key;

        var token = new ApnsJwt(options, new FakeTimeProvider(Now)).Token;
        var claims = JsonDocument.Parse(Base64Url(token.Split('.')[1])).RootElement;

        Assert.Equal("TEAM123456", claims.GetProperty("iss").GetString());
        Assert.Equal(Now.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
    }

    [Fact]
    public void The_signature_verifies_against_the_public_key()
    {
        var (options, key) = NewOptions();
        using var _ = key;

        var token = new ApnsJwt(options, new FakeTimeProvider(Now)).Token;
        var parts = token.Split('.');

        var verified = key.VerifyData(
            Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64Url(parts[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.True(verified);
    }

    [Fact]
    public void The_token_is_reused_until_it_ages_out()
    {
        var (options, key) = NewOptions();
        using var _ = key;
        var time = new FakeTimeProvider(Now);
        var jwt = new ApnsJwt(options, time);

        var first = jwt.Token;
        time.Advance(TimeSpan.FromMinutes(49));

        Assert.Equal(first, jwt.Token);
        Assert.Equal(1, jwt.Issued);
    }

    [Fact]
    public void A_new_token_is_minted_after_fifty_minutes()
    {
        var (options, key) = NewOptions();
        using var _ = key;
        var time = new FakeTimeProvider(Now);
        var jwt = new ApnsJwt(options, time);

        var first = jwt.Token;
        time.Advance(ApnsJwt.Lifetime);
        var second = jwt.Token;

        Assert.NotEqual(first, second);
        Assert.Equal(2, jwt.Issued);
    }

    [Fact]
    public void The_lifetime_stays_inside_apples_window()
    {
        Assert.InRange(ApnsJwt.Lifetime, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(60));
    }
}
