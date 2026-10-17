using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace Plugin.Maui.Spine.Server.Authentication.Tests;

/// <summary>
/// A platform authenticator in software: an ES256 key per passkey, "none" attestation, and the user handle it was given,
/// as iCloud Keychain answers.
/// </summary>
internal sealed class SoftwareAuthenticator(string origin, string rpId)
{
    private readonly Dictionary<string, (ECDsa Key, byte[] UserHandle, uint Counter)> _passkeys = [];

    public bool KeepsCounter { get; set; }

    public IReadOnlyCollection<string> Ids => _passkeys.Keys;

    /// <summary>Answers <c>navigator.credentials.create()</c> for the options' challenge and user.</summary>
    public string Create(string optionsJson, string? originOverride = null)
    {
        var options = JsonNode.Parse(optionsJson)!;
        var id = RandomNumberGenerator.GetBytes(16);
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var userHandle = Base64UrlEncoder.DecodeBytes((string)options["user"]!["id"]!);
        _passkeys[Base64UrlEncoder.Encode(id)] = (key, userHandle, 0);

        var parameters = key.ExportParameters(false);
        var cose = new CborWriter();
        cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2);
        cose.WriteInt32(3); cose.WriteInt32(-7);
        cose.WriteInt32(-1); cose.WriteInt32(1);
        cose.WriteInt32(-2); cose.WriteByteString(parameters.Q.X);
        cose.WriteInt32(-3); cose.WriteByteString(parameters.Q.Y);
        cose.WriteEndMap();

        using var authData = new MemoryStream();
        authData.Write(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        authData.WriteByte(0x01 | 0x04 | 0x40);
        authData.Write(new byte[4]);
        authData.Write(new byte[16]);
        authData.WriteByte(0);
        authData.WriteByte((byte)id.Length);
        authData.Write(id);
        authData.Write(cose.Encode());

        var attestation = new CborWriter();
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteTextString("authData"); attestation.WriteByteString(authData.ToArray());
        attestation.WriteEndMap();

        return new JsonObject
        {
            ["id"] = Base64UrlEncoder.Encode(id),
            ["rawId"] = Base64UrlEncoder.Encode(id),
            ["type"] = "public-key",
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = ClientData("webauthn.create", (string)options["challenge"]!, originOverride),
                ["attestationObject"] = Base64UrlEncoder.Encode(attestation.Encode()),
                ["transports"] = new JsonArray("internal", "hybrid"),
            },
            ["clientExtensionResults"] = new JsonObject(),
        }.ToJsonString();
    }

    /// <summary>Answers <c>navigator.credentials.get()</c> with the passkey <paramref name="id"/>, or the only one.</summary>
    public string Get(string optionsJson, string? id = null, byte[]? userHandle = null)
    {
        var options = JsonNode.Parse(optionsJson)!;
        id ??= _passkeys.Keys.Single();
        var (key, handle, counter) = _passkeys[id];
        if (KeepsCounter) _passkeys[id] = (key, handle, ++counter);

        using var authData = new MemoryStream();
        authData.Write(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        authData.WriteByte(0x01 | 0x04);
        authData.Write([(byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter]);

        var clientData = ClientData("webauthn.get", (string)options["challenge"]!, null);
        var signed = authData.ToArray().Concat(SHA256.HashData(Base64UrlEncoder.DecodeBytes(clientData))).ToArray();
        var signature = key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return new JsonObject
        {
            ["id"] = id,
            ["rawId"] = id,
            ["type"] = "public-key",
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = clientData,
                ["authenticatorData"] = Base64UrlEncoder.Encode(authData.ToArray()),
                ["signature"] = Base64UrlEncoder.Encode(signature),
                ["userHandle"] = Base64UrlEncoder.Encode(userHandle ?? handle),
            },
            ["clientExtensionResults"] = new JsonObject(),
        }.ToJsonString();
    }

    private string ClientData(string type, string challenge, string? originOverride) =>
        Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(new JsonObject
        {
            ["type"] = type,
            ["challenge"] = challenge,
            ["origin"] = originOverride ?? origin,
            ["crossOrigin"] = false,
        }.ToJsonString()));
}
