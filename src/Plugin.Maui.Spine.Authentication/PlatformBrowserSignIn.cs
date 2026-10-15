#if WINDOWS
using System.Net;
using System.Net.Sockets;
using System.Text;
#endif

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// The system browser: MAUI's <c>WebAuthenticator</c> on iOS, Mac Catalyst and Android, and on Windows an RFC 8252
/// loopback listener, because <c>WebAuthenticator</c> does not work there in .NET 10.
/// </summary>
#pragma warning disable CS9113 // Windows returns to a loopback address and needs none of the options.
internal sealed class PlatformBrowserSignIn(SpineAuthenticationOptions options) : IBrowserSignIn
#pragma warning restore CS9113
{
#if WINDOWS
    // A closed browser tab sends nothing back, so this is the only way the wait ends on Windows; long enough for a
    // provider's second factor, short enough that the sheet does not stay busy for long.
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

    public async Task<BrowserAnswer?> AuthenticateAsync(Func<Uri, Uri> start, CancellationToken cancellationToken)
    {
        // A socket rather than HttpListener: http.sys refuses http://127.0.0.1:port/ to a user without a URL ACL, and
        // the server takes only IP literals, not localhost (RFC 8252 §8.3). Port 0 lets the system pick a free port.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var callback = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");

        await Launcher.Default.OpenAsync(start(callback));

        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        giveUp.CancelAfter(GiveUpAfter);
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(giveUp.Token);
                var stream = client.GetStream();
                var target = await RequestTargetAsync(stream, giveUp.Token);

                // The browser may ask for /favicon.ico, and anything else on this machine may knock: only the
                // callback carries a state, and the flow checks that it is this sign-in's.
                var values = target is null ? [] : Values(target);
                if (!values.ContainsKey("state"))
                {
                    await RespondAsync(stream, "404 Not Found", "", giveUp.Token);
                    continue;
                }

                await RespondAsync(stream, "200 OK",
                    "<!doctype html><meta charset=utf-8><title>Signed in</title><p style=\"font:16px system-ui;margin:3em\">You can close this window and go back to the app.</p>",
                    giveUp.Token);
                return new BrowserAnswer(callback, values);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The request line's target, such as <c>/?code=…&amp;state=…</c>; the headers are not needed.</summary>
    private static async Task<string?> RequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) break;
            length += read;
            if (Array.IndexOf(buffer, (byte)'\n', 0, length) >= 0) break;
        }

        var line = Encoding.ASCII.GetString(buffer, 0, length).Split('\n')[0].Trim();
        var parts = line.Split(' ');
        return parts.Length == 3 && parts[0] == "GET" ? parts[1] : null;
    }

    private static Dictionary<string, string> Values(string target)
    {
        var query = target.IndexOf('?') is var at and >= 0 ? target[(at + 1)..] : "";
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .GroupBy(pair => Uri.UnescapeDataString(pair[0]))
            .ToDictionary(g => g.Key, g => g.First() is { Length: 2 } p ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "");
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string html, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }
#else
    public async Task<BrowserAnswer?> AuthenticateAsync(Func<Uri, Uri> start, CancellationToken cancellationToken)
    {
        if (options.CallbackScheme is not { Length: > 0 } scheme)
            throw new InvalidOperationException("Spine sign-in: a browser sign-in needs a.CallbackScheme, the app's URL scheme (and SpineAuthenticationCallbackScheme in the project for Android).");

        var callback = new Uri($"{scheme}://callback");
        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = start(callback),
                CallbackUrl = callback,
                PrefersEphemeralWebBrowserSession = options.EphemeralBrowserSession,
            }).WaitAsync(cancellationToken);

            return new BrowserAnswer(callback, result.Properties);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The user closed the browser sheet.
            return null;
        }
    }
#endif
}
