using WebKit;

namespace Orientera.Services.Eventor;

public static partial class EventorCookies
{
    /// <summary>
    /// iOS keeps the web view's cookies in <c>WKHTTPCookieStore</c>, HttpOnly ones included, and
    /// hands over the expiry date with them. That date is the only place the app can learn how long
    /// Eventor's "kom ihåg mig" actually lasts.
    /// </summary>
    public static partial async Task<IReadOnlyList<SessionCookie>> ReadAsync(WebView view)
    {
        if (view.Handler?.PlatformView is not WKWebView native)
            return [];

        var cookies = await native.Configuration.WebsiteDataStore.HttpCookieStore.GetAllCookiesAsync();

        return
        [
            .. cookies
                // Every cookie the federation's own domain sets, not only the ones spelled with
                // the "eventor" host. A parent-domain cookie — ".orientering.se" — belongs to
                // Eventor just as much, and the login is the one cookie that must not be dropped.
                .Where(c => c.Domain.TrimStart('.').EndsWith("orientering.se", StringComparison.OrdinalIgnoreCase))
                .Select(c => new SessionCookie(
                    c.Name,
                    c.Value,
                    c.ExpiresDate is { } expires ? (DateTimeOffset)(DateTime)expires : null)),
        ];
    }
}
