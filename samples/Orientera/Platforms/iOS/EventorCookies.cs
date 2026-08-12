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
                .Where(c => c.Domain.Contains("eventor.orientering.se", StringComparison.OrdinalIgnoreCase))
                .Select(c => new SessionCookie(
                    c.Name,
                    c.Value,
                    c.ExpiresDate is { } expires ? (DateTimeOffset)(DateTime)expires : null)),
        ];
    }
}
