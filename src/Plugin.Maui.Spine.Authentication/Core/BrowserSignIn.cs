using System.Security.Cryptography;
using System.Text;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>What the browser returned to: the redirect it was given and the parameters on it.</summary>
/// <param name="Callback">The redirect the sign-in returned to, to send again with the code.</param>
/// <param name="Values">The query (or fragment) parameters: <c>code</c>, <c>state</c>, <c>error</c>, …</param>
internal sealed record BrowserAnswer(Uri Callback, IReadOnlyDictionary<string, string> Values);

/// <summary>Runs a sign-in in the system browser and waits for it to return to the app.</summary>
internal interface IBrowserSignIn
{
    /// <param name="start">Builds the URL to open from the redirect the browser will return to.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The answer, or <see langword="null"/> when the user closed the browser.</returns>
    Task<BrowserAnswer?> AuthenticateAsync(Func<Uri, Uri> start, CancellationToken cancellationToken);
}

/// <summary>PKCE (RFC 7636): a verifier kept in the app and its S256 challenge sent ahead.</summary>
internal readonly record struct Pkce(string Verifier, string Challenge)
{
    public static Pkce Create()
    {
        var verifier = Random();
        return new Pkce(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    /// <summary>32 random bytes, base64url: a verifier, a state or a nonce.</summary>
    public static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Query(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
