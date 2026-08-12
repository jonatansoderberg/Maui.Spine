using Android.Webkit;

// Android.Webkit has a WebView of its own; the parameter is MAUI's.
using WebView = Microsoft.Maui.Controls.WebView;

namespace Orientera.Services.Eventor;

public static partial class EventorCookies
{
    /// <summary>
    /// Android's <c>CookieManager</c> answers with the cookie header and nothing more — no expiry,
    /// no flags. So a session captured here has no known lifetime, which is why nothing in the app
    /// depends on one: it asks the start page whether the login still works.
    /// </summary>
    public static partial Task<IReadOnlyList<SessionCookie>> ReadAsync(WebView view)
    {
        var header = CookieManager.Instance?.GetCookie(Origin);

        if (header is null or { Length: 0 })
            return Task.FromResult<IReadOnlyList<SessionCookie>>([]);

        var cookies = header
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => new SessionCookie(parts[0], parts[1], null))
            .ToList();

        return Task.FromResult<IReadOnlyList<SessionCookie>>(cookies);
    }
}
