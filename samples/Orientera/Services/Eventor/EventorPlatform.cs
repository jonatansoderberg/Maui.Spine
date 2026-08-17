namespace Orientera.Services.Eventor;

/// <summary>
/// Reads the cookies out of the web view the user logged in through.
/// </summary>
/// <remarks>
/// Platform APIs, because the session cookie is HttpOnly and JavaScript cannot see it — measured
/// while designing this (#123). iOS hands over expiry dates; Android's <c>CookieManager</c> gives
/// only name and value, so the longest-lived cookie is unknown there. That asymmetry is why the
/// app never depends on the expiry: it asks the start page whether it is still logged in.
/// </remarks>
public static partial class EventorCookies
{
    public static partial Task<IReadOnlyList<SessionCookie>> ReadAsync(WebView view);
}

/// <summary>
/// The login itself, kept on the phone.
/// </summary>
/// <remarks>
/// Stored so the app can log in again without asking, when Eventor's own persistent cookie has run
/// out. Two things about how it is replayed, both deliberate:
///
/// The password goes into <see cref="SecureStorage"/>, which is the Keychain on iOS and the
/// Keystore-backed store on Android, and nowhere else. It is never sent to our backend, because our
/// backend has no business holding it.
///
/// And it is replayed by filling Eventor's own form in the web view rather than by posting to
/// <c>/Login</c> with an <c>HttpClient</c>. The login page loads Cloudflare Turnstile; a raw post
/// works today and can stop working without warning, and it would fail <em>silently</em> — the
/// answer would be a login page, not an error. Driving the real form keeps that from happening and
/// survives two-factor if the federation adds it.
/// </remarks>
public sealed class EventorCredentialStore
{
    private const string UsernameKey = "eventor.username";
    private const string PasswordKey = "eventor.password";

    public async Task<(string Username, string Password)?> ReadAsync()
    {
        var username = await SecureStorage.Default.GetAsync(UsernameKey);
        var password = await SecureStorage.Default.GetAsync(PasswordKey);

        return username is { Length: > 0 } && password is { Length: > 0 } ? (username, password) : null;
    }

    public async Task SaveAsync(string username, string password)
    {
        await SecureStorage.Default.SetAsync(UsernameKey, username);
        await SecureStorage.Default.SetAsync(PasswordKey, password);
    }

    public void Forget()
    {
        SecureStorage.Default.Remove(UsernameKey);
        SecureStorage.Default.Remove(PasswordKey);
    }
}
