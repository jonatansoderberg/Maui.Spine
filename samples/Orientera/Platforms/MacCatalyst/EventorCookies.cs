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
                .Where(c => c.Domain.Contains("eventor.orientering.se", StringComparison.OrdinalIgnoreCase))
                .Select(c => new SessionCookie(
                    c.Name,
                    c.Value,
                    c.ExpiresDate is { } expires ? (DateTimeOffset)(DateTime)expires : null)),
        ];
    }
}
