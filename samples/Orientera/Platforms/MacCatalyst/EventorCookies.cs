using WebKit;

namespace Orientera.Services.Eventor;

/// <summary>Mac Catalyst runs the same WKWebView as iOS, so the same store answers.</summary>
public static partial class EventorCookies
{
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
                    c.ExpiresDate is { } expires ? (DateTimeOffset)(DateTime)expires : null)
                {
                    Domain = c.Domain,
                }),
        ];
    }

    public static partial async Task ForgetAsync()
    {
        var store = WKWebsiteDataStore.DefaultDataStore.HttpCookieStore;

        foreach (var cookie in await store.GetAllCookiesAsync())
        {
            if (cookie.Domain.TrimStart('.').EndsWith("orientering.se", StringComparison.OrdinalIgnoreCase))
                await store.DeleteCookieAsync(cookie);
        }
    }
}
