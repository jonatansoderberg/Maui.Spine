using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>What the server answered: tokens, an accepted step (an email went out), or a refusal.</summary>
/// <param name="Tokens">The tokens on 200.</param>
/// <param name="Status">The HTTP status, or 0 when the server could not be reached.</param>
/// <param name="Error">The server's <see cref="SpineAuthErrors"/> value, or a message naming the path and the status.</param>
/// <param name="Pending">A provider account the server holds until the user chooses (202 with a body).</param>
internal sealed record ServerAnswer(SpineTokens? Tokens, int Status, string? Error, SpinePendingIdentity? Pending = null)
{
    public bool Accepted => Status == (int)HttpStatusCode.Accepted;
}

/// <summary>Where a session trades its refresh token: the app's Spine server, or an identity provider directly.</summary>
internal interface ITokenEndpoint
{
    /// <param name="refreshToken">The token to trade.</param>
    /// <param name="user">Who it belongs to, for an endpoint whose answer does not say again.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new pair; status 401 when the endpoint refused the token for good.</returns>
    Task<ServerAnswer> RefreshAsync(string refreshToken, SpineUser? user, CancellationToken cancellationToken);

    Task<ServerAnswer> RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <summary>The calls to <c>MapSpineAuthentication</c>'s endpoints.</summary>
internal sealed class SpineAuthClient(HttpClient http, Uri root) : ITokenEndpoint
{
    public Uri Root => root;

    public async Task<SpineNonce?> NonceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(root, "nonce"), cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync((JsonTypeInfo<SpineNonce>)SpineAuthJson.Options.GetTypeInfo(typeof(SpineNonce)), cancellationToken)
                : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public Task<ServerAnswer> IdTokenAsync(string provider, SpineIdTokenRequest request, CancellationToken cancellationToken) =>
        PostAsync($"{provider}/token", request, cancellationToken);

    public Task<ServerAnswer> RefreshAsync(string refreshToken, SpineUser? user, CancellationToken cancellationToken) =>
        PostAsync("token", new SpineRefreshRequest(refreshToken), cancellationToken);

    public Task<ServerAnswer> CodeAsync(SpineCodeExchange exchange, CancellationToken cancellationToken) =>
        PostAsync("token", exchange, cancellationToken);

    public Task<ServerAnswer> RevokeAsync(string refreshToken, CancellationToken cancellationToken) =>
        PostAsync("revoke", new SpineRefreshRequest(refreshToken), cancellationToken);

    public Task<ServerAnswer> PasswordAsync<T>(string step, T body, CancellationToken cancellationToken) =>
        PostAsync($"password/{step}", body, cancellationToken);

    public async Task<ServerAnswer> DeleteAccountAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(root, "account"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync(request, cancellationToken);
    }

    public Task<ServerAnswer> LinkAsync(string provider, SpineIdTokenRequest request, string accessToken, CancellationToken cancellationToken) =>
        PostAsync($"link/{Uri.EscapeDataString(provider)}", request, cancellationToken, accessToken);

    public async Task<ServerAnswer> UnlinkAsync(string provider, string accessToken, CancellationToken cancellationToken, string? subject = null)
    {
        var path = $"link/{Uri.EscapeDataString(provider)}" + (subject is null ? "" : $"/{Uri.EscapeDataString(subject)}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(root, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync(request, cancellationToken);
    }

    /// <summary>A one-time ticket for linking through the browser, or a message naming the path and the status.</summary>
    public async Task<(SpineLinkTicket? Ticket, string? Error)> LinkTicketAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, "link/ticket"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode && await TryReadAsync<SpineLinkTicket>(response, cancellationToken) is { } ticket
                ? (ticket, null)
                : (null, $"POST {request.RequestUri} answered {(int)response.StatusCode}.");
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (null, $"POST {request.RequestUri}: {e.Message}");
        }
    }

    /// <summary><c>pending/create</c>, or <c>pending/link</c> with the access token of the account the user proved.</summary>
    public Task<ServerAnswer> PendingAsync(string action, SpinePendingIdentity pending, string? accessToken, CancellationToken cancellationToken) =>
        PostAsync($"pending/{action}", new SpinePendingRequest(pending.Ticket), cancellationToken, accessToken);

    /// <summary>
    /// <c>passkey/register/options</c> or <c>passkey/signin/options</c>: the WebAuthn options for the platform's sheet, or
    /// the server's error. A registration with <paramref name="accessToken"/> adds a passkey to that user; without one, it
    /// makes <paramref name="newAccount"/>.
    /// </summary>
    public async Task<(SpinePasskeyChallenge? Challenge, string? Error)> PasskeyOptionsAsync(
        string ceremony, SpinePasskeyNewAccount? newAccount, string? accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, $"passkey/{ceremony}/options"));
        if (newAccount is not null)
            request.Content = JsonContent.Create(newAccount, (JsonTypeInfo<SpinePasskeyNewAccount>)SpineAuthJson.Options.GetTypeInfo(typeof(SpinePasskeyNewAccount)));
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode && await TryReadAsync<SpinePasskeyChallenge>(response, cancellationToken) is { } challenge)
                return (challenge, null);

            return (null, (await TryReadAsync<SpineAuthProblem>(response, cancellationToken))?.Error
                ?? $"POST {request.RequestUri} answered {(int)response.StatusCode}.");
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (null, $"POST {request.RequestUri}: {e.Message}");
        }
    }

    /// <summary><c>passkey/register</c> or <c>passkey/signin</c>: the platform's credential, traded for tokens.</summary>
    public Task<ServerAnswer> PasskeyAsync(string ceremony, SpinePasskeyResponse response, CancellationToken cancellationToken) =>
        PostAsync($"passkey/{ceremony}", response, cancellationToken);

    private async Task<ServerAnswer> PostAsync<T>(string path, T body, CancellationToken cancellationToken, string? accessToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, path))
        {
            Content = JsonContent.Create(body, (JsonTypeInfo<T>)SpineAuthJson.Options.GetTypeInfo(typeof(T))),
        };
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync(request, cancellationToken);
    }

    private async Task<ServerAnswer> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Offline, or no answer in time: reported with the path, never as a refusal.
            return new ServerAnswer(null, 0, $"{request.Method} {request.RequestUri}: {e.Message}");
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.OK)
                return new ServerAnswer(await ReadAsync<SpineTokens>(response, cancellationToken), status, null);

            // A 202 is an email on its way, or, with a body, a provider account waiting for the user's choice.
            if (response.StatusCode == HttpStatusCode.Accepted && response.Content.Headers.ContentLength is not 0)
                return new ServerAnswer(null, status, null, await TryReadAsync<SpinePendingIdentity>(response, cancellationToken));

            if (response.IsSuccessStatusCode) return new ServerAnswer(null, status, null);

            // A refusal from Spine's endpoints names its error; anything else (a proxy, a wrong path) is reported with
            // the path and the status, so a misconfigured server does not look like a wrong password.
            var problem = await TryReadAsync<SpineAuthProblem>(response, cancellationToken);
            return new ServerAnswer(null, status, problem?.Error ?? $"{request.Method} {request.RequestUri} answered {status}.");
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync((JsonTypeInfo<T>)SpineAuthJson.Options.GetTypeInfo(typeof(T)), cancellationToken);

    private static async Task<T?> TryReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await ReadAsync<T>(response, cancellationToken);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
