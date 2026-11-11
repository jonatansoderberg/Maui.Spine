#if WINDOWS
using System.Buffers.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// Passkeys through Windows' WebAuthn API (<c>webauthn.dll</c>, Windows 10 1903 or later): Windows Hello, a phone or a
/// security key, in Windows' own dialog over the app's window.
/// </summary>
/// <remarks>
/// A desktop app may ask for any relying party: Windows has no association file and names the domain in its dialog.
/// The app writes <c>clientDataJSON</c> itself, with <c>https://&lt;rp id&gt;</c> as the origin, the origin the server
/// already accepts from iOS and macOS.
/// </remarks>
internal sealed partial class WindowsPasskeys : IPasskeyCeremony
{
    private const string Library = "webauthn.dll";
    private const uint DefaultTimeout = 60_000;

    private const int NteUserCancelled = unchecked((int)0x80090036);
    private const int ErrorCancelled = unchecked((int)0x800704C7);
    private const int NteExists = unchecked((int)0x8009000F);

    public static bool IsSupported { get; } = ApiVersion() >= 1;

    public async Task<string?> CreateAsync(string options, CancellationToken cancellationToken)
    {
        var json = JsonNode.Parse(options)!;
        var window = await WindowAsync();
        return await CallAsync(cancellation => MakeCredential(window, json, cancellation), cancellationToken);
    }

    public async Task<string?> GetAsync(string options, bool autoFill, CancellationToken cancellationToken)
    {
        var json = JsonNode.Parse(options)!;
        var window = await WindowAsync();
        return await CallAsync(cancellation => GetAssertion(window, json, cancellation), cancellationToken);
    }

    private static Task<nint> WindowAsync() => MainThread.InvokeOnMainThreadAsync(() =>
        Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window
            ? WinRT.Interop.WindowNative.GetWindowHandle(window)
            : throw new InvalidOperationException("The passkey dialog needs the app's window."));

    // The call blocks until the dialog closes, so it runs off the UI thread; the token cancels it through its id.
    private static async Task<string?> CallAsync(Func<Guid, (int Result, string? Credential)> call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cancellation = NewCancellationId();

        (int Result, string? Credential) outcome;
        using (cancellationToken.Register(() => Cancel(cancellation)))
            outcome = await Task.Run(() => call(cancellation), CancellationToken.None);

        cancellationToken.ThrowIfCancellationRequested();
        return outcome.Result switch
        {
            0 => outcome.Credential,
            NteUserCancelled or ErrorCancelled => null,
            NteExists => throw new InvalidOperationException("This device already has a passkey for the account."),
            var result => throw new InvalidOperationException($"The passkey dialog failed: {ErrorName(result)} (0x{result:X8})."),
        };
    }

    private static unsafe (int, string?) MakeCredential(nint window, JsonNode json, Guid cancellation)
    {
        using var native = new Arena();
        var rpNode = json["rp"]!;
        var rpId = (string)rpNode["id"]!;
        var userNode = json["user"]!;
        var userId = Decode(userNode["id"]);
        var userName = (string?)userNode["name"] ?? "";
        var selection = json["authenticatorSelection"];

        var rp = new RpEntity { Version = 1, Id = native.String(rpId), Name = native.String((string?)rpNode["name"] ?? rpId) };
        var user = new UserEntity
        {
            Version = 1,
            IdLength = (uint)userId.Length,
            Id = native.Bytes(userId),
            Name = native.String(userName),
            DisplayName = native.String((string?)userNode["displayName"] ?? userName),
        };

        // WebAuthn's defaults when the server names none: ES256 and RS256.
        var algorithms = (json["pubKeyCredParams"] as JsonArray ?? [])
            .Where(p => (string?)p?["type"] == "public-key")
            .Select(p => (int)p!["alg"]!)
            .DefaultIfEmpty(-7)
            .ToArray();
        var parameters = native.Array<CoseParameter>(algorithms.Length);
        for (var i = 0; i < algorithms.Length; i++)
            parameters[i] = new CoseParameter { Version = 1, Type = native.String("public-key"), Algorithm = algorithms[i] };
        var parameterList = new CoseParameters { Count = (uint)algorithms.Length, Items = parameters };

        var clientDataJson = ClientDataJson("webauthn.create", json["challenge"], rpId);
        var clientData = native.ClientData(clientDataJson);

        var makeOptions = new MakeCredentialOptions
        {
            Version = 3,
            TimeoutMilliseconds = (uint?)json["timeout"] ?? DefaultTimeout,
            CredentialList = native.Credentials(json["excludeCredentials"]),
            AuthenticatorAttachment = Attachment((string?)selection?["authenticatorAttachment"]),
            RequireResidentKey = (string?)selection?["residentKey"] == "required" || (bool?)selection?["requireResidentKey"] == true ? 1 : 0,
            UserVerificationRequirement = Verification((string?)selection?["userVerification"]),
            AttestationConveyancePreference = (string?)json["attestation"] switch { "indirect" => 2u, "direct" or "enterprise" => 3u, _ => 1u },
            CancellationId = &cancellation,
        };

        CredentialAttestation* attestation = null;
        var result = WebAuthNAuthenticatorMakeCredential(window, &rp, &user, &parameterList, &clientData, &makeOptions, &attestation);
        if (result != 0) return (result, null);

        try
        {
            var id = Encode(attestation->CredentialId, attestation->CredentialIdLength);
            var transport = Transport(attestation->Version >= 3 ? attestation->UsedTransport : 0);
            return (0, new JsonObject
            {
                ["id"] = id,
                ["rawId"] = id,
                ["type"] = "public-key",
                ["authenticatorAttachment"] = AttachmentOf(transport),
                ["response"] = new JsonObject
                {
                    ["clientDataJSON"] = Base64Url.EncodeToString(clientDataJson),
                    ["attestationObject"] = Encode(attestation->AttestationObject, attestation->AttestationObjectLength),
                    ["transports"] = transport is null ? new JsonArray() : new JsonArray(transport),
                },
                ["clientExtensionResults"] = new JsonObject(),
            }.ToJsonString());
        }
        finally
        {
            WebAuthNFreeCredentialAttestation(attestation);
        }
    }

    private static unsafe (int, string?) GetAssertion(nint window, JsonNode json, Guid cancellation)
    {
        using var native = new Arena();
        var rpId = (string)json["rpId"]!;
        var clientDataJson = ClientDataJson("webauthn.get", json["challenge"], rpId);
        var clientData = native.ClientData(clientDataJson);

        var getOptions = new GetAssertionOptions
        {
            Version = 4,
            TimeoutMilliseconds = (uint?)json["timeout"] ?? DefaultTimeout,
            CredentialList = native.Credentials(json["allowCredentials"]),
            UserVerificationRequirement = Verification((string?)json["userVerification"]),
            CancellationId = &cancellation,
        };

        Assertion* assertion = null;
        var result = WebAuthNAuthenticatorGetAssertion(window, native.String(rpId), &clientData, &getOptions, &assertion);
        if (result != 0) return (result, null);

        try
        {
            var id = Encode(assertion->Credential.Id, assertion->Credential.IdLength);
            return (0, new JsonObject
            {
                ["id"] = id,
                ["rawId"] = id,
                ["type"] = "public-key",
                ["authenticatorAttachment"] = AttachmentOf(Transport(assertion->Version >= 4 ? assertion->UsedTransport : 0)),
                ["response"] = new JsonObject
                {
                    ["clientDataJSON"] = Base64Url.EncodeToString(clientDataJson),
                    ["authenticatorData"] = Encode(assertion->AuthenticatorData, assertion->AuthenticatorDataLength),
                    ["signature"] = Encode(assertion->Signature, assertion->SignatureLength),
                    ["userHandle"] = assertion->UserIdLength == 0 ? null : Encode(assertion->UserId, assertion->UserIdLength),
                },
                ["clientExtensionResults"] = new JsonObject(),
            }.ToJsonString());
        }
        finally
        {
            WebAuthNFreeAssertion(assertion);
        }
    }

    private static byte[] ClientDataJson(string type, JsonNode? challenge, string rpId) => Encoding.UTF8.GetBytes(new JsonObject
    {
        ["type"] = type,
        ["challenge"] = (string?)challenge,
        ["origin"] = $"https://{rpId}",
        ["crossOrigin"] = false,
    }.ToJsonString());

    private static uint Attachment(string? attachment) => attachment switch { "platform" => 1, "cross-platform" => 2, _ => 0 };

    private static string? AttachmentOf(string? transport) => transport switch { null => null, "internal" => "platform", _ => "cross-platform" };

    private static uint Verification(string? requirement) => requirement switch { "required" => 1, "preferred" => 2, "discouraged" => 3, _ => 0 };

    private static string? Transport(uint transport) => transport switch
    {
        0x01 => "usb",
        0x02 => "nfc",
        0x04 => "ble",
        0x10 => "internal",
        0x20 => "hybrid",
        0x40 => "smart-card",
        _ => null,
    };

    private static byte[] Decode(JsonNode? base64Url) => Base64Url.DecodeFromChars(((string?)base64Url ?? "").AsSpan());

    private static unsafe string Encode(byte* bytes, uint length) => Base64Url.EncodeToString(new ReadOnlySpan<byte>(bytes, (int)length));

    private static uint ApiVersion()
    {
        try
        {
            return WebAuthNGetApiVersionNumber();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    private static unsafe Guid NewCancellationId()
    {
        Guid id;
        Marshal.ThrowExceptionForHR(WebAuthNGetCancellationId(&id));
        return id;
    }

    private static unsafe void Cancel(Guid id) => WebAuthNCancelCurrentOperation(&id);

    private static unsafe string ErrorName(int result) => new(WebAuthNGetErrorName(result));

    /// <summary>The native memory one call hands to webauthn.dll, freed together when the call returns.</summary>
    private sealed unsafe class Arena : IDisposable
    {
        private readonly List<nint> _blocks = [];

        public T* Array<T>(int count) where T : unmanaged
        {
            var block = (T*)NativeMemory.AllocZeroed((nuint)Math.Max(count, 1), (nuint)sizeof(T));
            _blocks.Add((nint)block);
            return block;
        }

        public byte* Bytes(ReadOnlySpan<byte> bytes)
        {
            var block = Array<byte>(bytes.Length);
            bytes.CopyTo(new Span<byte>(block, bytes.Length));
            return block;
        }

        public char* String(string text)
        {
            var block = Array<char>(text.Length + 1);
            text.CopyTo(new Span<char>(block, text.Length));
            return block;
        }

        public ClientDataInfo ClientData(byte[] json) =>
            new() { Version = 1, Length = (uint)json.Length, Json = Bytes(json), HashAlgorithm = String("SHA-256") };

        public Credentials Credentials(JsonNode? descriptors)
        {
            var ids = (descriptors as JsonArray ?? []).Where(c => c?["id"] is not null).Select(c => Decode(c!["id"])).ToArray();
            var items = Array<Credential>(ids.Length);
            for (var i = 0; i < ids.Length; i++)
                items[i] = new Credential { Version = 1, IdLength = (uint)ids[i].Length, Id = Bytes(ids[i]), Type = String("public-key") };
            return new Credentials { Count = (uint)ids.Length, Items = ids.Length == 0 ? null : items };
        }

        public void Dispose()
        {
            foreach (var block in _blocks) NativeMemory.Free((void*)block);
        }
    }

    // webauthn.h, API version 1: make-credential options v3, get-assertion options v4; the results are read only up to
    // the fields their version has.

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct RpEntity
    {
        public uint Version;
        public char* Id;
        public char* Name;
        public char* Icon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct UserEntity
    {
        public uint Version;
        public uint IdLength;
        public byte* Id;
        public char* Name;
        public char* Icon;
        public char* DisplayName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ClientDataInfo
    {
        public uint Version;
        public uint Length;
        public byte* Json;
        public char* HashAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CoseParameter
    {
        public uint Version;
        public char* Type;
        public int Algorithm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CoseParameters
    {
        public uint Count;
        public CoseParameter* Items;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Credential
    {
        public uint Version;
        public uint IdLength;
        public byte* Id;
        public char* Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Credentials
    {
        public uint Count;
        public Credential* Items;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Extensions
    {
        public uint Count;
        public void* Items;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MakeCredentialOptions
    {
        public uint Version;
        public uint TimeoutMilliseconds;
        public Credentials CredentialList;
        public Extensions Extensions;
        public uint AuthenticatorAttachment;
        public int RequireResidentKey;
        public uint UserVerificationRequirement;
        public uint AttestationConveyancePreference;
        public uint Flags;
        public Guid* CancellationId;
        public void* ExcludeCredentialList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct GetAssertionOptions
    {
        public uint Version;
        public uint TimeoutMilliseconds;
        public Credentials CredentialList;
        public Extensions Extensions;
        public uint AuthenticatorAttachment;
        public uint UserVerificationRequirement;
        public uint Flags;
        public char* U2fAppId;
        public int* U2fAppIdUsed;
        public Guid* CancellationId;
        public void* AllowCredentialList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CredentialAttestation
    {
        public uint Version;
        public char* FormatType;
        public uint AuthenticatorDataLength;
        public byte* AuthenticatorData;
        public uint AttestationLength;
        public byte* Attestation;
        public uint AttestationDecodeType;
        public void* AttestationDecode;
        public uint AttestationObjectLength;
        public byte* AttestationObject;
        public uint CredentialIdLength;
        public byte* CredentialId;
        public Extensions Extensions;
        public uint UsedTransport;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Assertion
    {
        public uint Version;
        public uint AuthenticatorDataLength;
        public byte* AuthenticatorData;
        public uint SignatureLength;
        public byte* Signature;
        public Credential Credential;
        public uint UserIdLength;
        public byte* UserId;
        public Extensions Extensions;
        public uint CredLargeBlobLength;
        public byte* CredLargeBlob;
        public uint CredLargeBlobStatus;
        public void* HmacSecret;
        public uint UsedTransport;
    }

    [LibraryImport(Library)]
    private static partial uint WebAuthNGetApiVersionNumber();

    [LibraryImport(Library)]
    private static unsafe partial int WebAuthNAuthenticatorMakeCredential(nint window, RpEntity* rp, UserEntity* user,
        CoseParameters* parameters, ClientDataInfo* clientData, MakeCredentialOptions* options, CredentialAttestation** attestation);

    [LibraryImport(Library)]
    private static unsafe partial int WebAuthNAuthenticatorGetAssertion(nint window, char* rpId, ClientDataInfo* clientData,
        GetAssertionOptions* options, Assertion** assertion);

    [LibraryImport(Library)]
    private static unsafe partial void WebAuthNFreeCredentialAttestation(CredentialAttestation* attestation);

    [LibraryImport(Library)]
    private static unsafe partial void WebAuthNFreeAssertion(Assertion* assertion);

    [LibraryImport(Library)]
    private static unsafe partial int WebAuthNGetCancellationId(Guid* id);

    [LibraryImport(Library)]
    private static unsafe partial int WebAuthNCancelCurrentOperation(Guid* id);

    [LibraryImport(Library)]
    private static unsafe partial char* WebAuthNGetErrorName(int result);
}
#endif
