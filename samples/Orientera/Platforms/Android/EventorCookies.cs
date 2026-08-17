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
        var header = CookieManager.Instance?.GetCookie(EventorSite.Origin);

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

    /// <summary>
    /// <c>CookieManager</c> has no way to drop one domain's cookies, and the store is shared by
    /// every web view in the process. Eventor is the only site the app ever opens one for, so
    /// emptying it entirely removes exactly what a per-domain call would have.
    /// </summary>
    public static partial Task ForgetAsync()
    {
        var manager = CookieManager.Instance;

        if (manager is null)
            return Task.CompletedTask;

        var done = new TaskCompletionSource();
        manager.RemoveAllCookies(new Callback(done));
        manager.Flush();

        return done.Task;
    }

    private sealed class Callback(TaskCompletionSource _done) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) => _done.TrySetResult();
    }
}
