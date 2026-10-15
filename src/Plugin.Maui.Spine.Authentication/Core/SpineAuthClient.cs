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
internal sealed record ServerAnswer(SpineTokens? Tokens, int Status, string? Error)
{
    public bool Accepted => Status == (int)HttpStatusCode.Accepted;
}

/// <summary>The calls to <c>MapSpineAuthentication</c>'s endpoints.</summary>
internal sealed class SpineAuthClient(HttpClient http, Uri root)
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

    public Task<ServerAnswer> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        PostAsync("token", new SpineRefreshRequest(refreshToken), cancellationToken);

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

    private async Task<ServerAnswer> PostAsync<T>(string path, T body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, path))
        {
            Content = JsonContent.Create(body, (JsonTypeInfo<T>)SpineAuthJson.Options.GetTypeInfo(typeof(T))),
        };
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
            if (response.StatusCode == HttpStatusCode.OK && request.Method != HttpMethod.Delete)
                return new ServerAnswer(await ReadAsync<SpineTokens>(response, cancellationToken), status, null);

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
